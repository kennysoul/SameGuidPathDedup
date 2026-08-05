# Same-GUID-Path Dedup

Emby server plugin that removes duplicate rows from `MediaItems` where two rows share
both the same **GUID** and the same **Path**. This is an Emby internal bug pattern:
when identification switches languages (e.g. English from filename → Chinese from TMDB
zh-CN), Emby creates a parallel DB row for the same logical item.

This plugin was developed and validated against an Emby **4.9.5** server whose library
sits on a WebDAV / FUSE mount of `115网盘` (Chinese cloud drive) where the bug
triggered most often during initial file sync. Other mount types also see it.

## Compatibility

| Target | Status |
| --- | --- |
| Emby 4.8.x | ⚠️ compiles, runtime untested |
| Emby 4.9.0 – 4.9.5 | ✅ **tested in production** (gzserver, 77 rows deduped on first run) |
| Emby 5.x | ⚠️ unverified (Emby 5 is still beta; plugin SDK changes are likely) |
| Jellyfin | ❌ (different plugin SDK — would need a separate fork) |
| Linux x64 (Debian / Ubuntu) | ✅ tested in production on .NET 8 runtime |
| macOS / Windows | ⚠️ should work (single `netstandard2.1` DLL, no platform deps), but untested |

The build produces **one** `SameGuidPathDedup.dll` (~21 KB, `netstandard2.1` MSIL) that
loads on every supported platform via Emby's in-process plugin loader.

> **Important**: `netstandard2.1` requires .NET Core 3.0+ or .NET 5+ runtime. This
> rules out legacy Mono. Emby 4.9.x ships on .NET 8, so this is fine.

## Plugin GUID

Hard-coded: `58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809`.

The DLL is named to match the GUID-derived hash; Emby stores per-plugin config under
`/var/lib/emby/plugins/configurations/<md5-of-guid>/<AssemblyName>.xml`.

> **No runtime GUID override**. Earlier versions of this plugin had a
> `PluginIdOverride` config field, but it was removed because reading `Configuration`
> from the `Id` getter triggers `BasePlugin<T>.LoadConfiguration()`, which calls
> `Path.Combine(path1, Path.GetFileName(Assembly.Location))`. When the DLL is loaded
> from a path where `Assembly.Location` is null (single-file, in-memory load, some
> FUSE filesystems), `Path.Combine` throws `ArgumentNullException` and the plugin
> fails to load entirely. If you need to change the GUID, fork the repo, edit
> `Plugin.DefaultPluginGuid`, and rebuild.

## Detection rule

```sql
SELECT Path, COUNT(*)
FROM MediaItems
WHERE Type IN (5, 10, 11, 14, 8, 9)   -- Movie, Series, Episode, MusicVideo, Audio, …
  AND Path IS NOT NULL
  AND Path <> ''
GROUP BY Path
HAVING COUNT(*) > 1
  AND COUNT(DISTINCT GUID) = 1;       -- defensive: never merge legitimate multi-version
```

Per group, the row to keep is picked by this priority (lowest rank wins):

1. Item with `DateModified > MinValue` (had real updates, not a freshly-created stub)
2. Item with at least one external provider ID (TMDB / IMDB / TVDB)
3. Item with `PremiereDate` set
4. Item with a non-zero `CommunityRating`
5. Fallback: most recent `DateCreated`, then smallest C# object reference (`ReferenceEquals`)

> **Why `ReferenceEquals`?** Emby's bug produces two physical DB rows that share the
> same `GUID` value. `BaseItem.Id` maps to that GUID column, so `i.Id != kept.Id` is
> always false for both rows. `GetItemList` returns distinct `BaseItem` instances
> for each physical row even when their `Id` values collide, so reference identity
> correctly identifies "this is a different DB row" regardless of the shared GUID.

## Safety guarantee

Verified directly against the live server before this plugin was deployed. On the
`gzserver` Emby instance (Emby 4.9.5, ~30k leaf items), there are **73 duplicate-Path
groups, all 73 have `COUNT(DISTINCT GUID) = 1`** — meaning **zero legitimate
multi-version content** would be touched. Legitimate multi-version (when you
intentionally have `movie-cd1.mkv` and `movie-cd2.mkv` for the same movie) uses
**different GUIDs** and is never matched.

## Build

Builds are produced automatically by GitHub Actions on every push to `main`. The
DLL is attached as a build artifact. For local builds:

```bash
./build.sh
# → artifacts/1.0.0.0/SameGuidPathDedup.dll
```

Requires .NET SDK 8.0+ on the build machine. Output is `netstandard2.1` MSIL —
architecture-neutral, runs on every supported platform.

## Install

> **Where to put the DLL**: Emby's plugin loader scans **only**
> `/var/lib/emby/plugins/*.dll` directly. Subdirectories are for plugin data, not
> for DLL hosting. Don't put the DLL inside `SameGuidPathDedup/1.0.0.0/`.

### Linux

```bash
# 1. Back up your library database first
sudo systemctl stop emby-server
sudo cp /var/lib/emby/data/library.db /var/lib/emby/data/library.db.bak-$(date +%Y%m%d)

# 2. Drop the DLL into the plugins folder
sudo cp SameGuidPathDedup.dll /var/lib/emby/plugins/SameGuidPathDedup.dll
sudo chown emby:emby /var/lib/emby/plugins/SameGuidPathDedup.dll

# 3. Start Emby
sudo systemctl start emby-server
```

### macOS (launchd)

```bash
# Adjust paths to match your Emby install. Common locations:
#   /Applications/EmbyServer.app
#   /opt/emby-server
# Adjust to your EmbyServer data path.
EMBY_DATA="$HOME/Library/Application Support/Emby-Server"   # check your install
DATA_PATH="$(plutil -extract ProgramArguments.1 raw -o - "$HOME/Library/LaunchAgents/com.emby.server.plist" 2>/dev/null || true)"
# …or just find the data dir once via Dashboard → Server → About.

sudo launchctl unload "$HOME/Library/LaunchAgents/com.emby.server.plist"
cp SameGuidPathDedup.dll "$EMBY_DATA/plugins/SameGuidPathDedup.dll"
sudo chown "$USER":staff "$EMBY_DATA/plugins/SameGuidPathDedup.dll"
sudo launchctl load "$HOME/Library/LaunchAgents/com.emby.server.plist"
```

### Windows (Service)

```bat
:: Stop the Emby Server service (Services → Emby Server → Stop, or:)
net stop "EmbyServer"

:: Drop the DLL into the plugins folder. Default data path:
::   C:\ProgramData\Emby-Server\plugins
copy SameGuidPathDedup.dll "C:\ProgramData\Emby-Server\plugins\SameGuidPathDedup.dll"

net start "EmbyServer"
```

### Verify it loaded

```bash
sudo grep -a "SameGuidPathDedup" /var/lib/emby/logs/embyserver.txt | tail -5
```

You should see:

```
… Info App: Loading SameGuidPathDedup, Version=0.0.0.0, …
… Info SameGuidPathDedup: [SameGuidPathDedup] Plugin loaded. GUID=58a3ade8-…
```

(Version `0.0.0.0` is normal — see *Why "0.0.0.0" Version?* below.)

## First run (DryRun = true by default)

The plugin ships with `DryRun=true`. The first real pass is a no-op; it only logs
what it *would* delete. This is a safe default — install the plugin and let it run
once with DryRun to see the candidate set before flipping the switch.

### Step 1: confirm the ScheduledTask was registered

Emby exposes scheduled tasks over the REST API. You'll need an admin API key
(Dashboard → Advanced → API Keys → Add). Save it to a variable:

```bash
API_KEY=your-admin-api-key
curl -s "http://localhost:8096/emby/ScheduledTasks?api_key=$API_KEY" \
  | python3 -m json.tool | grep -B1 -A4 "Same-GUID-Path"
```

You should see something like:

```json
{
    "Name": "Same-GUID-Path Dedup",
    "State": "Idle",
    "Id": "f3646d0e33e145c7cddde6ea89987cd2",
    "Triggers": [ { "Type": "IntervalTrigger", "IntervalTicks": 9000000000 } ],
    "Key": "SameGuidPathDedup"
}
```

Note the `"Id"` — you'll use it to trigger runs manually.

### Step 2: trigger a dry-run

```bash
TASK_ID="f3646d0e33e145c7cddde6ea89987cd2"   # from step 1
curl -s -X POST \
  "http://localhost:8096/emby/ScheduledTasks/Running/$TASK_ID?api_key=$API_KEY"
sleep 5
sudo grep -a "SameGuidPathDedup" /var/lib/emby/logs/embyserver.txt | tail -10
```

You should see lines like:

```
[SameGuidPathDedup] DRY-RUN would delete item (Id=…, Name='…')
…
[SameGuidPathDedup] Pass complete. groups=N, deleted=N, failed=0, DryRun=True
```

### Step 3: read the candidate list yourself

```bash
sudo grep -a "DRY-RUN would delete" /var/lib/emby/logs/embyserver.txt \
  | sed 's/.*DRY-RUN //' > /tmp/candidates.txt
wc -l /tmp/candidates.txt
less /tmp/candidates.txt
```

Every line has the form:

```
would delete item (Id=<guid>, Name='<name>', DateModified=<date>, DateCreated=<date>)
 ; keeping (Id=<guid>, Name='<name>', DateModified=<date>, HasProviderIds=True)
 Path='<path>'
```

Verify that the **kept** row on every line is the one you want to keep. A
healthy pass keeps the row with `HasProviderIds=True` and discards the empty stub.
If you see any line where the kept row looks wrong (e.g. keeps a stub with no
metadata, discards the row with TMDB data), **stop and file an issue** before
turning DryRun off.

### Step 4: switch to real deletes

```bash
curl -s -X POST \
  -H "Content-Type: application/json" \
  -d '{"DryRun":false,"ScanIntervalMinutes":15,"EnableScheduledTask":true,"EnablePostScanHook":true,"DeleteBatchSize":50,"MinItemAgeSeconds":60,"WhitelistPaths":[],"PluginIdOverride":""}' \
  "http://localhost:8096/emby/Plugins/58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809/Configuration?api_key=$API_KEY"
```

A `204 No Content` response means the config is now live. Verify:

```bash
curl -s "http://localhost:8096/emby/Plugins/58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809/Configuration?api_key=$API_KEY"
```

The `DryRun` field should now read `false`.

### Step 5: trigger the real pass and verify

```bash
curl -s -X POST \
  "http://localhost:8096/emby/ScheduledTasks/Running/$TASK_ID?api_key=$API_KEY"
sleep 30
sudo grep -a "SameGuidPathDedup" /var/lib/emby/logs/embyserver.txt | tail -10
```

The final summary line should look like:

```
[SameGuidPathDedup] Pass complete. groups=N, deleted=N, failed=0, DryRun=False
```

### Step 6: confirm zero duplicates remain

```bash
sudo sqlite3 /var/lib/emby/data/library.db \
  "SELECT COUNT(*) FROM (SELECT Path FROM MediaItems
   WHERE Path IS NOT NULL AND Path<>'' AND Type IN (5,10,11,14,8,9)
   GROUP BY Path HAVING COUNT(*) > 1)"
# expected: 0
```

### Step 7 (optional): roll back if anything went wrong

```bash
sudo systemctl stop emby-server
sudo cp /var/lib/emby/data/library.db.bak-YYYYMMDD /var/lib/emby/data/library.db
sudo chown emby:emby /var/lib/emby/data/library.db
sudo systemctl start emby-server
```

(The backup is a hot copy made while Emby was stopped, so it's consistent and
restores byte-for-byte.)

## Configuration reference

All fields are JSON-encoded in the per-plugin config XML (managed by Emby).

| Key | Type | Default | Description |
| --- | --- | --- | --- |
| `DryRun` | bool | `true` | When true, log candidates only; never delete. Flip to false after reviewing. |
| `ScanIntervalMinutes` | int | `15` | ScheduledTask cadence. Editable from Dashboard or via REST. |
| `EnableScheduledTask` | bool | `true` | Disable to keep only the PostScan hook. |
| `EnablePostScanHook` | bool | `true` | Disable to keep only the scheduled task. |
| `DeleteBatchSize` | int | `50` | Upper bound per pass; protects against long transactions. (Reserved — current implementation processes everything in one transaction.) |
| `MinItemAgeSeconds` | int | `60` | Skip items newer than this. Protects against racing Emby's own metadata writes during a scan. |
| `WhitelistPaths` | string[] | `[]` | Path prefixes to exclude. Use this to leave a specific library untouched. |
| `PluginIdOverride` | string | `""` | **Deprecated, ignored.** Config exists for backward compatibility; the plugin uses a hard-coded GUID. |

Update config via REST (Dashboard UI in Emby 4.9.x doesn't surface this plugin's config page because we don't ship a `IPluginConfigurationPage`):

```bash
curl -s -X POST -H "Content-Type: application/json" \
  -d @new-config.json \
  "http://localhost:8096/emby/Plugins/58a3ade8-ca3f-4b2b-b036-a0ccb3d3f809/Configuration?api_key=$API_KEY"
```

## What the plugin does not do (deliberately)

- **No metadata cross-fill.** Emby already joins rows by GUID internally; the
  surviving row already represents the union of both. There is no metadata to
  rescue from the doomed row.
- **No multi-version file merge.** When you have intentionally stored
  `movie-cd1.mkv` + `movie-cd2.mkv` for the same movie, Emby uses **different
  GUIDs** for those (that's how `EnableMultiVersionByFiles` works). They never
  share a Path, so the plugin never matches them.
- **No container-type dedup.** Folders, Seasons, BoxSets (Type 3, 4, 13, etc.)
  are not scanned. Their GUID reuse is intentional.
- **No file deletion.** `DeleteOptions.DeleteFile = false` is enforced. The
  plugin only removes DB rows; your media files on disk are untouched.
- **No ownership of the API key.** You bring your own admin API key from
  Dashboard → Advanced → API Keys.

## Operational notes

### Why "0.0.0.0" Version?

Emby's plugin loader reports `Version=0.0.0.0` because we don't ship a `git describe`
in the build. The plugin reads its own assembly version via `AssemblyVersion`, but
Emby doesn't pass that to its loader. Don't be alarmed by the version string.

### Why no REST endpoints?

We tried shipping `/webapi/plugins/sameguidpathdedup/{preview,run,config}` endpoints.
Emby 4.9.x's public plugin SDK does not expose the `[Authenticated]` attribute
(lives in `Emby.Api.dll`, not the public SDK package), so REST endpoints need
basic-auth or session-cookie auth instead. We chose to drop the REST endpoints
and document the direct `Emby/Plugins/.../Configuration` and
`Emby/ScheduledTasks/Running/...` APIs instead, which work out of the box with
an admin API key.

### Why does the DryRun line list the same GUID for both `delete` and `keep`?

Because `BaseItem.Id` is the DB GUID and the bug gives both physical rows the same
GUID value. The two rows differ only as C# object instances. `ReferenceEquals`
correctly distinguishes them; `Id != Id` would not.

### PostScan hook fires on every library scan

In addition to the 15-minute cadence, the plugin runs immediately after every
Emby library scan completes. The intent is to catch duplicates that the scan
itself created before the next scheduled run. Disable with
`EnablePostScanHook: false` if you want a quieter log.

### How to disable the plugin temporarily

Easiest: stop the Emby server, rename the DLL
(`/var/lib/emby/plugins/SameGuidPathDedup.dll` →
`/var/lib/emby/plugins/SameGuidPathDedup.dll.disabled`), restart Emby.

To permanently uninstall: stop Emby, `sudo rm /var/lib/emby/plugins/SameGuidPathDedup.dll`,
restart. The plugin does not touch the library database on uninstall.

### Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `Loading SameGuidPathDedup …` then nothing | DLL architecture mismatch | Verify DLL is `netstandard2.1`. On Linux ARM/old Mono, Emby can't load netstandard2.1. |
| `Assembly.Location is null` exception | Plugin DLL was loaded from a path the .NET runtime can't resolve a Location for | Move the DLL into `/var/lib/emby/plugins/` and restart. |
| `Pass complete. groups=N, deleted=N, failed=0` and groups keeps growing | New files keep triggering the bug | This is expected behaviour; the plugin will keep cleaning them up every 15 min. |
| `failed > 0` | A delete failed (e.g. due to a permissions race or a cascade-delete that already removed the item) | Inspect the `Failed to delete item …` log lines for the exception. Items are processed in isolation; one failure does not block the rest. |
| Emby says `Plugin SameGuidPathDedup failed to load.` | Missing dependency, GUID conflict, or wrong target framework | Check `/var/lib/emby/logs/embyserver.txt` for the exception details. |
| Plugin loads but Emby never runs it | ScheduledTask disabled | Set `EnableScheduledTask: true` in config. |

## License

MIT — see `LICENSE`.

## Background

This plugin was born from an investigation into duplicate movies appearing on an
Emby server whose library sits on a WebDAV / FUSE mount of `115网盘` (Chinese
cloud drive). Initial investigation showed:

1. The duplicates were always one English-named and one Chinese-named row
   pointing at the same file (filename = English, TMDB zh-CN = Chinese).
2. Disk-side multi-version (`movie-cd1.mkv` vs `movie-cd2.mkv`) was **not** the
   cause — Emby treats those as legitimate multi-version, not as duplicates.
3. Both rows in a duplicate pair share the exact same `GUID` value in the
   `MediaItems` table.

Conclusion: the root cause is **in Emby's identification flow** (not on disk,
not in the FUSE layer), specifically when language-mismatched metadata refresh
creates a second row for the same logical item instead of updating the first.

The fix is post-scan cleanup: since both rows are already joined by GUID inside
Emby's data model, the surviving row already represents the union — we only
need to drop the empty stub.

## Changelog

### 1.0.0 (2026-08)

- First production release. Validated on Emby 4.9.5 with 77 deduped rows
  (3 movie-level + 74 TV-series/episode-level duplicates).