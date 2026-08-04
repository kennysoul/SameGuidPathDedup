using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Scheduling;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace SameGuidPathDedup
{
    /// <summary>
    /// Periodic dedup runner. Registered as an Emby ScheduledTask so admins can:
    ///   - see it under Dashboard → Scheduled Tasks,
    ///   - change the interval (IConfigurableScheduledTask),
    ///   - click "Run" for an on-demand trigger.
    ///
    /// The default interval is set in PluginConfiguration and overridden here so that
    /// changes in the Dashboard UI are honored.
    /// </summary>
    public class DedupScheduledTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IItemRepository _itemRepository;
        private readonly IPluginManager _pluginManager;
        private readonly DedupEngine _engine;

        public DedupScheduledTask(
            ILibraryManager libraryManager,
            IUserManager userManager,
            IItemRepository itemRepository,
            IPluginManager pluginManager,
            ILogManager logManager)
        {
            _logger = logManager.GetLogger("SameGuidPathDedup");
            _libraryManager = libraryManager;
            _userManager = userManager;
            _itemRepository = itemRepository;
            _pluginManager = pluginManager;
            _engine = new DedupEngine(_logger, _libraryManager, _userManager, _itemRepository);
        }

        public string Name => "Same-GUID-Path Dedup";
        public string Key => "SameGuidPathDedup";
        public string Description =>
            "Merges duplicate MediaItems rows that share the same GUID and Path.";
        public string Category => "Library";

        public bool IsHidden => false;
        public bool IsEnabled => true;
        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromMinutes(15).Ticks,
                    MaxRuntimeTicks = TimeSpan.FromMinutes(5).Ticks
                }
            };
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var config = ResolveConfig();
            return _engine.RunOnceAsync(
                source: "ScheduledTask",
                config: config,
                progress: progress,
                cancellationToken: cancellationToken);
        }

        private PluginConfiguration ResolveConfig()
        {
            var plugin = _pluginManager?.Plugins?.OfType<Plugin>().FirstOrDefault();
            return plugin?.Configuration ?? new PluginConfiguration();
        }
    }
}