using System;
using System.Collections.Generic;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;

namespace SameGuidPathDedup
{
    /// <summary>
    /// Plugin entry point. Loads on every Emby startup.
    ///
    /// GUID override strategy:
    ///   In Emby 4.9.x there is no longer a public IPluginManager that plugins can
    ///   use to enumerate peer plugins. We therefore cannot detect a GUID collision
    ///   at construction time. Instead, the admin can set PluginIdOverride in the
    ///   plugin's config to a new GUID; Emby uses that on the next restart.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public static readonly Guid DefaultPluginGuid =
            Guid.Parse("58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809");

        /// <summary>
        /// Singleton accessor. Set in the constructor (BasePlugin's constructor is
        /// called first; we assign Instance on the first line of our body). Other
        /// classes in the plugin assembly (tasks, REST service) read this to get
        /// the live configuration.
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

        public override IEnumerable<PluginPageInfo> GetPages()
        {
            // No custom configuration pages for now. Edit config XML directly
            // under /var/lib/emby/plugins/SameGuidPathDedup/1.0.0.0/config.xml.
            yield break;
        }
    }
}