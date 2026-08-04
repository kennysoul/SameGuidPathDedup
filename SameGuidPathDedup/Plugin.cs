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
    ///   Emby's BasePlugin reads its Configuration at construction time to
    ///   derive the plugin's configuration file path. Calling
    ///   this.Configuration from the Id override triggers that load early,
    ///   and the load can throw if Assembly.Location is null. Therefore the
    ///   Id override must NOT touch Configuration at all. DefaultPluginGuid
    ///   is the canonical value.
    ///
    ///   Admins who need to override the GUID can rename this plugin's
    ///   directory or build a custom fork. The PluginIdOverride config key
    ///   is no longer used; it remains in PluginConfiguration for
    ///   backward compatibility with the design doc but is ignored.
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

        public override Guid Id => DefaultPluginGuid;

        public override string Description =>
            "Merges duplicate MediaItems rows that share the same GUID and Path. " +
            "Triggered manually, every 15 minutes, and after every library scan.";
    }
}