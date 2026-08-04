# Same-GUID-Path Dedup

Emby server plugin that removes duplicate rows from `MediaItems` where two rows share
both the same **GUID** and the same **Path**. This is an Emby internal bug pattern where
language-mismatched identification (e.g. English from filename, Chinese from TMDB zh-CN)
creates a parallel DB row for the same logical item.

Detection is intentionally conservative: only items with `Type IN (5, 10, 11, 14)` and
the same `Path` are candidates. Legitimate multi-version items (same movie, different
file) use **different GUIDs** and are never touched.

## Compatibility

| Target | Status |
| --- | --- |
| Emby 4.8.x | ✅ tested |
| Emby 4.9.x | ✅ tested |
| Emby 5.x | ⚠️ unverified, may need rebase (ServerEntryPoint / LibraryManager signatures) |
| Jellyfin | ❌ (different plugin SDK) |
| .NET Core / Mono runtime | ✅ (`netstandard2.0` produces architecture-neutral MSIL) |
| Linux x64 / ARM64 | ✅ |
| macOS x64 / ARM64 | ✅ |
| Windows | ✅ |

The build produces **one** `SameGuidPathDedup.dll` (netstandard2.0, ~30KB) that loads on
every supported platform via Emby's in-process plugin loader.

## Plugin GUID

Hard-coded: `58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809`

The plugin detects GUID conflicts at startup and either logs a warning (if it can still
load) or throws `PluginLoadException` (forcing Emby to mark the plugin failed in the
Dashboard). If you need to override the GUID:

1. Open Emby Dashboard → Plugins → Same-GUID-Path Dedup → gear icon → Edit settings.
2. Set `PluginIdOverride` to a new GUID.
3. Restart the Emby server.

You can also override by editing `/var/lib/emby/plugins/SameGuidPathDedup/1.0.0.0/config.xml`
directly if the plugin cannot start.

## Build

The repository ships a GitHub Actions workflow that builds the plugin on every push and
attaches `SameGuidPathDedup.dll` as a build artifact.

For local builds:

```bash
./build.sh
# → artifacts/1.0.0.0/SameGuidPathDedup.dll
```

Requires .NET SDK 6.0 or newer on the build machine. The output DLL is independent of
the SDK version (target framework is `netstandard2.0`).

## Install (target Emby server)

```bash
# 1. Stop Emby
sudo systemctl stop emby-server

# 2. Create plugin directory
sudo mkdir -p /var/lib/emby/plugins/SameGuidPathDedup/1.0.0.0

# 3. Drop the DLL
sudo cp SameGuidPathDedup.dll /var/lib/emby/plugins/SameGuidPathDedup/1.0.0.0/

# 4. Fix ownership
sudo chown -R emby:emby /var/lib/emby/plugins/SameGuidPathDedup/

# 5. Start Emby
sudo systemctl start emby-server
```

The plugin will appear under Dashboard → Plugins. Two new entries also appear in
Scheduled Tasks:
- **Same-GUID-Path Dedup** (manual + 15-minute auto)
- *(No PostScanTask row in the UI; it fires automatically after every library scan)*

## First Run (DryRun = true by default)

1. Dashboard → Scheduled Tasks → **Same-GUID-Path Dedup** → Run.
2. Watch `/var/lib/emby/logs/embyserver.txt` for `[SameGuidPathDedup]` lines.
3. Verify the report shows the candidates you expected (typically a small handful of
   `Movie` rows pointing to the same path under two different names).
4. If satisfied, Dashboard → Plugins → Same-GUID-Path Dedup → set `DryRun = false`.
5. Run the task again. Items in the report are deleted via `ILibraryManager.DeleteItem`
   (which cascades through `AncestorIds2`, `ItemExtradata`, `ItemLinks2`, etc.).

## Configuration

| Key | Default | Description |
| --- | --- | --- |
| `DryRun` | `true` | When true, log candidates instead of deleting. |
| `ScanIntervalMinutes` | `15` | Scheduled task interval. |
| `EnableScheduledTask` | `true` | Disable to keep only the PostScan hook. |
| `EnablePostScanHook` | `true` | Disable to keep only the scheduled task. |
| `DeleteBatchSize` | `50` | Maximum items deleted per run (transaction boundary). |
| `MinItemAgeSeconds` | `60` | Skip items younger than this — protects against racing Emby's own metadata writes during a scan. |
| `WhitelistPaths` | `[]` | Path prefixes to exclude (e.g. `["/mnt/hdd/Movie"]` to never dedupe that library). |
| `PluginIdOverride` | `""` | Override the plugin GUID if the default collides with another plugin. |

## What it doesn't do (deliberately)

- **Does not merge metadata fields** between items. Both rows are joined by GUID inside
  Emby already, so the surviving row is the one Emby itself considers authoritative. If
  you need cross-row metadata rescue (rare), use `RemoteSearch/Apply` manually on the
  surviving row before deleting the other.
- **Does not dedupe by Path alone**. Two items with the same Path but different GUIDs
  is the *legitimate* multi-version case (`EnableMultiVersionByFiles=true`). The plugin
  ignores those.
- **Does not modify Folder / Season / BoxSet items** (Type 3, 4, 13, etc.). Those are
  organizational containers; their GUID collisions are intentional (multiple Paths per
  CollectionFolder is normal).
- **Does not touch user data** (play state, ratings). `DeleteItem` does cascade that,
  so use DryRun carefully.

## License

MIT (or whatever you set in `LICENSE`).

## Background

This plugin was born from an investigation into duplicate movies appearing on an Emby
server whose library sits on a WebDAV / FUSE mount of `115网盘` (Chinese cloud drive).
The duplicates were always one English-named row and one Chinese-named row pointing at
the same file. Investigation showed both rows share the same `GUID` — Emby itself
treats them as one logical item — so merging is lossless.