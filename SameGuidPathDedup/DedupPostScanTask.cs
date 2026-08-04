using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;

namespace SameGuidPathDedup
{
    /// <summary>
    /// Fires immediately after every library scan finishes.
    /// Pairs with <see cref="DedupScheduledTask"/>; either can be disabled in the
    /// plugin configuration. Both call into the same <see cref="DedupEngine"/>.
    /// </summary>
    public class DedupPostScanTask : ILibraryPostScanTask
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IItemRepository _itemRepository;
        private readonly DedupEngine _engine;

        public DedupPostScanTask(
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

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            return _engine.RunOnceAsync(
                source: "PostScanTask",
                config: config,
                progress: progress,
                cancellationToken: cancellationToken);
        }
    }
}