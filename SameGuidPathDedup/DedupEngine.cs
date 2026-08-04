using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using SameGuidPathDedup.Models;

namespace SameGuidPathDedup
{
    /// <summary>
    /// Core dedup logic. Pure-ish (depends only on Emby public APIs); no reflection.
    /// Safe to call from ScheduledTask, PostScanTask, or REST entry point.
    ///
    /// Detection rule (matches the SQL we verified against the live server):
    ///   GROUP BY GUID HAVING COUNT(*) > 1 AND COUNT(DISTINCT Path) = 1
    ///   restricted to leaf item types (Movie / Series / Episode / MusicVideo)
    ///
    /// Whichever passes the rule, exactly one row is kept — Emby treats all rows that
    /// share a GUID as the same logical entity, so the surviving row already represents
    /// the union. No metadata cross-fill is required.
    /// </summary>
    public class DedupEngine
    {
        // Emby internal Type IDs. See Emby.Server.Implementations.Entities.ItemTypeKindMap.
        private static readonly int[] LeafItemTypes =
        {
            5,   // Movie
            10,  // (varies by Emby version — included for cross-version safety)
            11,  // Episode (sometimes Audio too)
            14   // Series / MusicVideo depending on version
        };

        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IItemRepository _itemRepository;

        public DedupEngine(
            ILogger logger,
            ILibraryManager libraryManager,
            IUserManager userManager,
            IItemRepository itemRepository)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _userManager = userManager;
            _itemRepository = itemRepository;
        }

        public async Task<DedupReport> RunOnceAsync(
            string source,
            PluginConfiguration config,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            var report = new DedupReport
            {
                Source = source,
                StartedAt = DateTime.UtcNow
            };

            try
            {
                if (config == null)
                {
                    _logger.Warn("[SameGuidPathDedup] No config supplied; skipping.");
                    return report;
                }

                if (!config.DryRun && source == "ScheduledTask" && !config.EnableScheduledTask)
                {
                    _logger.Info("[SameGuidPathDedup] ScheduledTask disabled in config; skipping.");
                    return report;
                }
                if (!config.DryRun && source == "PostScanTask" && !config.EnablePostScanHook)
                {
                    _logger.Info("[SameGuidPathDedup] PostScanHook disabled in config; skipping.");
                    return report;
                }

                _logger.Info(
                    $"[SameGuidPathDedup] Begin pass (source={source}, DryRun={config.DryRun})");

                var candidates = await ScanForCandidatesAsync(config, cancellationToken).ConfigureAwait(false);
                report.GroupsFound = candidates.Count;

                foreach (var group in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var keepCandidate = group.Keep;
                    foreach (var doomedItem in group.DeleteItems)
                    {
                        var doomedCandidate = DedupCandidate.From(doomedItem);
                        report.ItemsDeleted++;

                        if (config.DryRun)
                        {
                            _logger.Info(
                                $"[SameGuidPathDedup] DRY-RUN would delete item " +
                                $"(Id={doomedCandidate.Id}, Name='{doomedCandidate.Name}', " +
                                $"DateModified={doomedCandidate.DateModified}, DateCreated={doomedCandidate.DateCreated}) " +
                                $"; keeping (Id={keepCandidate.Id}, Name='{keepCandidate.Name}', " +
                                $"DateModified={keepCandidate.DateModified}, HasProviderIds={keepCandidate.HasProviderIds}) " +
                                $"Path='{group.Path}' GUID={group.Guid}");
                            continue;
                        }

                        try
                        {
                            DeleteItem(doomedItem);
                            _logger.Info(
                                $"[SameGuidPathDedup] Deleted item " +
                                $"(Id={doomedCandidate.Id}, Name='{doomedCandidate.Name}') " +
                                $"Path='{group.Path}' GUID={group.Guid} " +
                                $"(kept Id={keepCandidate.Id})");
                        }
                        catch (Exception ex)
                        {
                            _logger.ErrorException(
                                $"[SameGuidPathDedup] Failed to delete item " +
                                $"(Id={doomedCandidate.Id}, Name='{doomedCandidate.Name}')", ex);
                            report.ItemsFailed++;
                        }

                        progress?.Report(report.ItemsDeleted);
                    }
                }

                _logger.Info(
                    $"[SameGuidPathDedup] Pass complete. " +
                    $"groups={report.GroupsFound}, " +
                    $"deleted={report.ItemsDeleted}, failed={report.ItemsFailed}, " +
                    $"DryRun={config.DryRun}");
            }
            catch (OperationCanceledException)
            {
                _logger.Info("[SameGuidPathDedup] Pass cancelled.");
            }
            catch (Exception ex)
            {
                _logger.ErrorException("[SameGuidPathDedup] Pass failed", ex);
            }
            finally
            {
                report.FinishedAt = DateTime.UtcNow;
            }

            return report;
        }

        /// <summary>
        /// Lists all leaf items via IItemRepository, groups by GUID, applies the
        /// detection rule, ranks each group, returns deletion candidates.
        /// </summary>
        private Task<List<DedupGroup>> ScanForCandidatesAsync(
            PluginConfiguration config,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var query = new InternalItemsQuery
                {
                    Recursive = true,
                    ItemTypes = LeafItemTypes
                    // Do NOT set Limit: IItemRepository.GetItemList returns ALL items
                    // that match the query (it doesn't paginate). Set Limit only if
                    // you need to call GetItems() (paginated).
                };

                var all = _itemRepository.GetItemList(query) ?? Enumerable.Empty<BaseItem>();

                var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var minAgeEpoch = nowEpoch - config.MinItemAgeSeconds;

                var groups =
                    all
                        .Where(i => i != null && i.Path != null && i.Path.Length > 0)
                        .Where(i => !IsWhitelisted(i.Path, config.WhitelistPaths))
                        .Where(i => ToEpoch(i.DateCreated) <= minAgeEpoch)
                        .GroupBy(i => i.Guid)
                        .Where(g => g.Key != Guid.Empty && g.Count() > 1)
                        .ToList();

                var result = new List<DedupGroup>();

                foreach (var grp in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Detection rule: same GUID AND same Path.
                    // Different Paths within the same GUID group = legitimate multi-version.
                    var items = grp.ToList();
                    var distinctPaths = items.Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    if (distinctPaths != 1) continue;

                    var path = items[0].Path;
                    var kept = items
                        .OrderBy(i => Rank(i))
                        .ThenByDescending(i => ToEpoch(i.DateCreated))
                        .ThenBy(i => i.Id)
                        .First();
                    var doomed = items.Where(i => i.Id != kept.Id).ToList();

                    if (doomed.Count == 0) continue;

                    result.Add(new DedupGroup
                    {
                        Guid = grp.Key,
                        Path = path,
                        KeepItem = kept,
                        DeleteItems = doomed
                    });
                }

                return result;
            }, cancellationToken);
        }

        private static int Rank(BaseItem item)
        {
            // Lower rank wins.
            int rank = 100;

            // Prefer items that have actually been written at least once
            // (DateModified == DateTime.MinValue means "never touched since create").
            if (item.DateModified > DateTime.MinValue) rank -= 10;

            // Prefer items with at least one external provider ID (TMDB / IMDB / TVDB).
            if (item.ProviderIds != null && item.ProviderIds.Count > 0) rank -= 10;

            // Strongly prefer items with a PremiereDate set.
            if (item.PremiereDate.HasValue) rank -= 5;

            // Slightly prefer items with a community rating.
            if (item.CommunityRating.HasValue && item.CommunityRating.Value > 0) rank -= 5;

            return rank;
        }

        private static bool IsWhitelisted(string path, string[] whitelist)
        {
            if (whitelist == null || whitelist.Length == 0) return false;
            foreach (var prefix in whitelist)
            {
                if (string.IsNullOrEmpty(prefix)) continue;
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static long ToEpoch(DateTime dt) =>
            dt == DateTime.MinValue ? 0L : new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeSeconds();

        /// <summary>
        /// Hard-delete a leaf item from the Emby database.
        /// Emby's DeleteItem cascades through AncestorIds2, ItemExtradata, ItemLinks2,
        /// ItemPeople2, UserDatas, etc. — we don't touch those tables directly.
        /// </summary>
        private void DeleteItem(BaseItem item)
        {
            var options = new DeleteOptions
            {
                DeleteFromDatabase = true,
                DeleteFile = false,        // we are NOT deleting the underlying media file
                DeleteRefreshState = true,
                DeleteChapterImages = false
            };

            _libraryManager.DeleteItem(item, options);
        }
    }
}