using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace SameGuidPathDedup
{
    /// <summary>
    /// Plugin entry point. Loads on every Emby startup.
    ///
    /// GUID conflict strategy:
    ///   1. If config.PluginIdOverride is set to a valid GUID, use that as the plugin's
    ///      GUID instead of the hard-coded default. Lets admins recover from a collision
    ///      without rebuilding the DLL.
    ///   2. On load, walk IPluginManager.Plugins and look for any plugin that has the
    ///      same GUID but a different Name. If found, throw PluginLoadException so the
    ///      failure surfaces in the Dashboard instead of being silently swallowed.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public static readonly Guid DefaultPluginGuid =
            Guid.Parse("58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809");

        private readonly ILogger _logger;
        private readonly ILogManager _logManager;
        private readonly IPluginManager _pluginManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IItemRepository _itemRepository;
        private readonly IJsonSerializer _jsonSerializer;

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            IPluginManager pluginManager,
            ILogManager logManager,
            ILibraryManager libraryManager,
            IUserManager userManager,
            IItemRepository itemRepository,
            IJsonSerializer jsonSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            _pluginManager = pluginManager;
            _logManager = logManager;
            _logger = logManager.GetLogger("SameGuidPathDedup");
            _libraryManager = libraryManager;
            _userManager = userManager;
            _itemRepository = itemRepository;
            _jsonSerializer = jsonSerializer;

            DetectGuidCollision();
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

        /// <summary>
        /// Register REST endpoints. Emby picks these up via
        /// <c>BasePlugin&lt;T&gt;.GetServices()</c> on plugin load.
        /// </summary>
        public override IEnumerable<IRestfulService> GetServices()
        {
            return new IRestfulService[]
            {
                new DedupRestService(
                    _libraryManager,
                    _userManager,
                    _itemRepository,
                    _pluginManager,
                    _jsonSerializer,
                    _logManager)
            };
        }

        private void DetectGuidCollision()
        {
            try
            {
                var loaded = _pluginManager?.Plugins;
                if (loaded == null) return;

                var ours = Id;
                var oursName = Name;

                foreach (var other in loaded)
                {
                    if (other.Id == ours && other.Name == oursName) continue;

                    if (other.Id == ours)
                    {
                        var msg =
                            $"GUID COLLISION detected: this plugin ('{oursName}' = {ours}) " +
                            $"shares its GUID with already-loaded plugin " +
                            $"'{other.Name}' ({other.Id}). " +
                            $"Resolution: open Emby Dashboard → Plugins → " +
                            $"'{other.Name}' → Advanced → change its Plugin Id, " +
                            $"OR set 'PluginIdOverride' in this plugin's config " +
                            $"and restart the Emby server.";

                        _logger.Error(msg);
                        throw new PluginLoadException(msg);
                    }
                }
            }
            catch (PluginLoadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Don't let the collision check itself block plugin load.
                _logger.WarnException("GUID collision detection failed", ex);
            }
        }
    }
}