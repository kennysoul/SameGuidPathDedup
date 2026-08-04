using System;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace SameGuidPathDedup
{
    /// <summary>
    /// Plugin entry point. Loads on every Emby startup.
    ///
    /// GUID override strategy:
    ///   In Emby 4.9.x the public plugin surface does not expose IPluginManager,
    ///   so we cannot enumerate peer plugins to detect a GUID collision at
    ///   construction time. Admins who run into a GUID conflict can set
    ///   PluginIdOverride in the plugin's config (a new GUID) and restart Emby;
    ///   the next load picks up the new GUID.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public static readonly Guid DefaultPluginGuid =
            Guid.Parse("58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809");

        /// <summary>
        /// Singleton accessor. Set in the constructor; other classes in the plugin
        /// assembly (DedupScheduledTask, DedupPostScanTask) read this to get the
        /// live configuration.
        /// </summary>
        public static Plugin Instance { get; private set; }

        private readonly ILogger _logger;

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILogManager logManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _logger = logManager.GetLogger("SameGuidPathDedup");

            _logger.Info("[SameGuidPathDedup] Plugin loaded. GUID={0}", Id);
        }

        public override string Name => "Same-GUID-Path Dedup";

        public override Guid Id
        {
            get
            {
                var overrideStr = Configuration?.PluginIdOverride;
                if (!string.IsNullOrWhiteSpace(overrideStr)
                    && Guid.TryParse(overrideStr.Trim(), out var overrideGuid))
                {
                    return overrideGuid;
                }
                return DefaultPluginGuid;
            }
        }

        public override string Description =>
            "Merges duplicate MediaItems rows that share the same GUID and Path. " +
            "Triggered manually, every 15 minutes, and after every library scan.";
    }
}