using MediaBrowser.Model.Plugins;

namespace SameGuidPathDedup
{
    /// <summary>
    /// Dashboard-editable configuration for the SameGuidPathDedup plugin.
    ///
    /// Surfaced under: Emby Dashboard → Plugins → Same-GUID-Path Dedup → gear icon.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// When true, the plugin only logs candidate duplicates and never deletes.
        /// Flip to false after reviewing logs in
        /// <c>/var/lib/emby/logs/embyserver.txt</c>.
        /// </summary>
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// Scheduled task cadence. Editable from Emby Dashboard → Scheduled Tasks.
        /// Default 15 minutes — usually faster than Emby itself notices the duplicate
        /// exists in the UI.
        /// </summary>
        public int ScanIntervalMinutes { get; set; } = 15;

        /// <summary>
        /// Enable the periodic ScheduledTask. Disable if you only want post-scan runs.
        /// </summary>
        public bool EnableScheduledTask { get; set; } = true;

        /// <summary>
        /// Enable the post-library-scan hook (fires every time Emby finishes scanning a
        /// library). Usually you want both the scheduled task and this hook.
        /// </summary>
        public bool EnablePostScanHook { get; set; } = true;

        /// <summary>
        /// Upper bound on the number of items deleted in a single pass. Prevents long
        /// transactions and lets Emby interleave its own work between batches.
        /// </summary>
        public int DeleteBatchSize { get; set; } = 50;

        /// <summary>
        /// Skip items newer than this many seconds. Protects against racing Emby's own
        /// metadata writes during a scan (an item that was just identified should not
        /// be deleted out from under the scan mid-flight).
        /// </summary>
        public int MinItemAgeSeconds { get; set; } = 60;

        /// <summary>
        /// Path prefixes that the plugin should never touch. Useful if you have a
        /// library where duplicate rows are intentional (e.g. competing scrapers
        /// you're A/B testing).
        /// </summary>
        public string[] WhitelistPaths { get; set; } = new string[0];

        /// <summary>
        /// Override the plugin GUID. Leave empty unless Emby logs a GUID collision at
        /// startup. Format: <c>58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809</c>.
        /// Requires a server restart to take effect.
        /// </summary>
        public string PluginIdOverride { get; set; } = "";
    }
}