# Immich Tagger

Immich Tagger is the new product name and direction for the former PhotoAIApp project.

```text
AI-powered tags, descriptions, and XMP sidecars for Immich photo libraries.
```

Immich Tagger scans photo folders, sends images to an Ollama vision model, and writes Immich-friendly XMP sidecars plus JSON diagnostics. It is designed for Windows desktop use today and is being migrated toward an Unraid/Docker server app so library tagging jobs can run in the background without tying up a Windows PC.

## Current release

The current published Windows builds may still use the legacy PhotoAIApp name while the rename/server migration is in progress.

Release page:

https://github.com/rzrnaz/PhotoAIApp/releases/tag/v0.1.31

Windows GUI assets:

- Self-contained publish ZIP: `PhotoAIApp_v0.1.31_AggregateSummaryPolish_win-x64_self-contained.zip`
- Standalone executable: `PhotoAIApp.Gui_v0.1.31_AggregateSummaryPolish_win-x64_self-contained.exe`

The ZIP contains the Windows GUI executable at:

```text
PhotoAIApp.Gui.exe
```

Future releases should move toward:

```text
ImmichTagger.exe
ghcr.io/rzrnaz/immich-tagger
/mnt/user/appdata/immich-tagger
```

## Current features

- WinForms GUI for selecting one folder or checkbox-selected multiple folders from a folder tree.
- Folder source defaults to `P:\` and auto-loads the folder tree when available.
- Dry-run/preview mode before touching production folders.
- Estimated dry-run processing time based only on files that would actually be processed.
- Writes compact internal `.photoai` JSON audit sidecars.
- Writes Immich-style `original-file.ext.xmp` sidecars.
- Preserves protected/existing descriptions unless overwrite behavior is intentionally enabled.
- XMP keyword/tag output is now the primary product value and should be enabled by default going forward.
- Preset-first AI settings with an Advanced settings dialog:
  - High Quality: local `qwen2.5vl:7b` full-resolution path.
  - Balanced: local `qwen2.5vl:7b` 1440px path.
  - Compatibility: Unraid `minicpm-v:latest` path.
  - Custom primary/fallback endpoint and model settings.
- Qwen primary retry hardening for transient Ollama input-ingestion failures before fallback.
- Unraid MiniCPM-V fallback support.
- Pause/resume for long runs, separate from Stop/cancel.
- Aggregate progress, ETA, retry/fallback counters, summaries, and logs across multi-folder runs.
- Final summaries and anomaly logs include combined multi-folder totals and one-decimal average seconds per processed photo.
- Live GUI log is human-readable, auto-scrolls to the newest entry, and avoids noisy repeated process lines.
- `Open Log` stays available for completion/cancellation paths when an anomaly log exists.
- Custom app icon and warm minimalist GUI theme.
- Scanner excludes internal `.photoai`, `.Recycle.Bin`, and `@eaDir` folders.

## Docker/Unraid direction

The planned Docker version should not run the existing Windows WinForms GUI inside a container. Instead, the project should become:

```text
PhotoAIApp.Core      shared scanner/model/sidecar engine
PhotoAIApp.Gui       existing Windows desktop GUI
PhotoAIApp.Server    new Docker/Unraid web UI/API host
PhotoAIApp or CLI    command-line runner for scripted jobs
```

Planned Docker defaults:

```text
Image:        ghcr.io/rzrnaz/immich-tagger:latest
Web UI:       http://server-ip:8080
/photos:      photo library or Immich external-library share
/config:      persistent appdata/config/logs
PUID:         99
PGID:         100
UMASK:        000
```

See:

- `docs/plans/2026-05-27-immich-tagger-docker-migration.md`
- `docs/user-manual.md`
- `docs/developer-manual.md`
- `docker/README.md`
- `docker/unraid/immich-tagger.xml`

## Safety model

Use **Dry Run** first when testing a production folder. Immich Tagger excludes its own `.photoai` folders so generated audit sidecars are not recursively scanned.

For Immich, after creating new sidecars beside already-known external-library assets, run metadata sidecar **Discover** first so Immich associates the new XMP files. Use **Sync** later for edits to sidecars Immich already knows about.

## Development

Build/test from the solution root:

```powershell
dotnet run --project .\PhotoAIApp.Tests\PhotoAIApp.Tests.csproj
dotnet build .\PhotoAIApp.sln
```

For a standalone Windows GUI executable:

```powershell
dotnet publish .\PhotoAIApp.Gui\PhotoAIApp.Gui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\PhotoAIApp-gui-win-x64-self-contained
```

When publishing from Linux, add Windows targeting:

```bash
dotnet publish ./PhotoAIApp.Gui/PhotoAIApp.Gui.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/PhotoAIApp-gui-win-x64-self-contained
```
