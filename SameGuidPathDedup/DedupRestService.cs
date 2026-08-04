using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using SameGuidPathDedup.Models;

namespace SameGuidPathDedup
{
    /// <summary>
    /// REST entry points. Mounted under /webapi/plugins/sameguidpathdedup/...
    ///
    /// Why this exists: admins sometimes want to trigger a dedup pass from a shell
    /// script or monitor, not from the Dashboard. The endpoints below let you do that.
    ///
    /// All endpoints require Admin authentication (enforced by [Authenticated]).
    /// </summary>
    [Authenticated(Roles = "Admin")]
    public class DedupRestService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IItemRepository _itemRepository;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly DedupEngine _engine;

        public DedupRestService(
            ILibraryManager libraryManager,
            IUserManager userManager,
            IItemRepository itemRepository,
            IJsonSerializer jsonSerializer,
            ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _userManager = userManager;
            _itemRepository = itemRepository;
            _jsonSerializer = jsonSerializer;
            _logger = logManager.GetLogger("SameGuidPathDedup");
            _engine = new DedupEngine(_logger, _libraryManager, _userManager, _itemRepository);
        }

        /// <summary>
        /// Always runs a dry-run pass and returns the JSON report.
        /// No items are deleted regardless of the plugin's configured DryRun flag.
        /// </summary>
        [Route("/webapi/plugins/sameguidpathdedup/preview", "GET")]
        public async Task<object> GetPreview()
        {
            var config = ResolveConfig();
            config.DryRun = true; // override for preview
            var report = await _engine.RunOnceAsync(
                source: "RestPreview",
                config: config,
                progress: null,
                cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            return ToDto(report);
        }

        /// <summary>
        /// Runs a real pass (respects plugin-config DryRun; default is DryRun=true so
        /// this is also safe by default).
        /// </summary>
        [Route("/webapi/plugins/sameguidpathdedup/run", "POST")]
        public async Task<object> PostRun()
        {
            var config = ResolveConfig();
            var report = await _engine.RunOnceAsync(
                source: "RestRun",
                config: config,
                progress: null,
                cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            return ToDto(report);
        }

        /// <summary>
        /// Reads the plugin's current configuration as JSON.
        /// </summary>
        [Route("/webapi/plugins/sameguidpathdedup/config", "GET")]
        public object GetConfig()
        {
            return ResolveConfig();
        }

        private PluginConfiguration ResolveConfig()
        {
            return Plugin.Instance?.Configuration ?? new PluginConfiguration();
        }

        private object ToDto(DedupReport report)
        {
            return new
            {
                source = report.Source,
                startedAt = report.StartedAt,
                finishedAt = report.FinishedAt,
                durationMs = report.Duration.TotalMilliseconds,
                groupsFound = report.GroupsFound,
                itemsDeleted = report.ItemsDeleted,
                itemsFailed = report.ItemsFailed,
                groups = report.Groups.Select(g => new
                {
                    guid = g.Guid,
                    path = g.Path,
                    keep = g.Keep == null
                        ? null
                        : new { g.Keep.Id, g.Keep.Name, g.Keep.DateModified, g.Keep.HasProviderIds },
                    delete = g.Delete.Select(d => new { d.Id, d.Name, d.DateModified }).ToArray()
                }).ToArray()
            };
        }
    }
}