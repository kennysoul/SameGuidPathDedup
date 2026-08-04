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
    /// Core dedup logic. Depends only on Emby public APIs; no reflection.
    /// Safe to call from ScheduledTask, PostScanTask, or REST entry point.
    ///
    /// Detection rule (Path-only; matches the SQL we verified against the live
    /// server):
    ///   GROUP BY Path HAVING COUNT(*) > 1
    ///   restricted to leaf item types (Movie / Series / Episode / MusicVideo)
    ///
    /// Multi-version files (legitimate Emby feature) have different Paths, so
    /// they are never matched. Bug duplicates from Emby's identification flow
    /// share the same Path, so they are always matched.
    /// </summary>
    public class DedupEngine
    {
        // Emby 4.9.x internal Type IDs (ItemType enum values). Verified against
        // a live MediaItems dump: 5 = Movie, 10/11/14 cover the other leaf types
        // that Emby treats as "replaceable" duplicates.
        private static readonly string[] LeafTypeStrings =
        {
            "Movie",
            "Series",
            "Episode",
            "MusicVideo",
            "Audio"
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

                _logger.Info(
                    $"[SameGuidPathDedup] Scan complete. groups={report.GroupsFound}");

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
                                $"Path='{group.Path}'");
                            continue;
                        }

                        try
                        {
                            DeleteItem(doomedItem);
                            _logger.Info(
                                $"[SameGuidPathDedup] Deleted item " +
                                $"(Id={doomedCandidate.Id}, Name='{doomedCandidate.Name}') " +
                                $"Path='{group.Path}' " +
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
        /// Lists all leaf items via IItemRepository, groups by Path, applies
        /// the detection rule, ranks each group, returns deletion candidates.
        /// </summary>
        private Task<List<DedupGroup>> ScanForCandidatesAsync(
            PluginConfiguration config,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                // IItemRepository.GetItemList returns ALL items that match
                // (no pagination). InternalItemsQuery.IncludeItemTypes takes
                // string[] of the Emby Type names (Movie, Series, ...).
                var query = new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = LeafTypeStrings
                };

                var all = _itemRepository.GetItemList(query) ?? Enumerable.Empty<BaseItem>();
                _logger.Info(
                    $"[SameGuidPathDedup] GetItemList returned {all.Count()} items");

                var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var minAgeEpoch = nowEpoch - config.MinItemAgeSeconds;

                var groups =
                    all
                        .Where(i => i != null && i.Path != null && i.Path.Length > 0)
                        .Where(i => !IsWhitelisted(i.Path, config.WhitelistPaths))
                        .Where(i => i.DateCreated.ToUnixTimeSeconds() <= minAgeEpoch)
                        .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() > 1)
                        .ToList();
                _logger.Info(
                    $"[SameGuidPathDedup] Path groups: {groups.Count} groups have duplicates");

                var result = new List<DedupGroup>();
                int scannedCount = 0, keptCount = 0;

                foreach (var grp in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var items = grp.ToList();
                    scannedCount++;

                    var kept = items
                        .OrderBy(i => Rank(i))
                        .ThenByDescending(i => i.DateCreated.ToUnixTimeSeconds())
                        .ThenBy(i => i.Id.ToString())  // i.Id is Guid; ToString for stable order
                        .First();
                    var doomed = items.Where(i => i.Id != kept.Id).ToList();

                    if (doomed.Count == 0) continue;

                    keptCount++;
                    result.Add(new DedupGroup
                    {
                        Path = grp.Key,
                        KeepItem = kept,
                        DeleteItems = doomed
                    });
                }

                _logger.Info(
                    $"[SameGuidPathDedup] After Rank filter: scanned={scannedCount}, kept={keptCount}, result={result.Count}");

                return result;
            }, cancellationToken);
        }

        private static int Rank(BaseItem item)
        {
            // Lower rank wins.
            int rank = 100;

            // Prefer items that have actually been written at least once
            // (DateModified == DateTimeOffset.MinValue means "never touched since create").
            if (item.DateModified > DateTimeOffset.MinValue) rank -= 10;

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

        /// <summary>
        /// Hard-delete a leaf item from the Emby database.
        /// Emby's DeleteItem cascades through AncestorIds2, ItemExtradata, ItemLinks2,
        /// ItemPeople2, UserDatas, etc. — we don't touch those tables directly.
        /// </summary>
        private void DeleteItem(BaseItem item)
        {
            // Default DeleteOptions is fine — Emby deletes the row from the
            // DB and cascades, but does not touch the underlying media file.
            // (The plugin never deletes files; only DB rows.)
            _libraryManager.DeleteItem(item, new DeleteOptions());
        }
    }
}