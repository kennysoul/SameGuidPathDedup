using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.ScheduledTasks;
using MediaBrowser.Model.Logging;

namespace SameGuidPathDedup
{
    /// <summary>
    /// Periodic dedup runner. Registered as an Emby ScheduledTask so admins can:
    ///   - see it under Dashboard → Scheduled Tasks,
    ///   - change the interval (Dashboard configures this automatically because
    ///     we implement IScheduledTask),
    ///   - click "Run" for an on-demand trigger.
    ///
    /// The default interval is 15 minutes. Changes via the Dashboard are persisted
    /// to /var/lib/emby/plugins/SameGuidPathDedup/1.0.0.0/tasks.xml.
    /// </summary>
    public class DedupScheduledTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IItemRepository _itemRepository;
        private readonly DedupEngine _engine;

        public DedupScheduledTask(
            ILibraryManager libraryManager,
            IUserManager userManager,
            IItemRepository itemRepository,
            ILogManager logManager)
        {
            _logger = logManager.GetLogger("SameGuidPathDedup");
            _libraryManager = libraryManager;
            _userManager = userManager;
            _itemRepository = itemRepository;
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
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            return _engine.RunOnceAsync(
                source: "ScheduledTask",
                config: config,
                progress: progress,
                cancellationToken: cancellationToken);
        }
    }
}