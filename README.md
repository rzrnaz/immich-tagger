# PhotoAIApp

PhotoAIApp is a Windows/.NET tool for generating PhotoAI audit sidecars and Immich-compatible XMP sidecars for selected photo folders.

## Current release

Release page:

https://github.com/rzrnaz/PhotoAIApp/releases/tag/v0.1.31

Windows GUI assets:

- Self-contained publish ZIP: `PhotoAIApp_v0.1.31_AggregateSummaryPolish_win-x64_self-contained.zip`
- Standalone executable: `PhotoAIApp.Gui_v0.1.31_AggregateSummaryPolish_win-x64_self-contained.exe`

The ZIP contains the Windows GUI executable at:

```text
PhotoAIApp.Gui.exe
```

## Current features

- WinForms GUI for selecting one folder or checkbox-selected multiple folders from a folder tree.
- Folder source defaults to `P:\` and auto-loads the folder tree when available.
- Dry-run/preview mode before touching production folders.
- Estimated dry-run processing time based only on files that would actually be processed.
- Writes compact internal `.photoai` JSON audit sidecars.
- Writes Immich-style `original-file.ext.xmp` sidecars.
- Preserves protected/existing descriptions unless overwrite behavior is intentionally enabled.
- Optional XMP keyword/tag fields via **Add Tags**.
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
- Custom PhotoAI app icon and warm minimalist GUI theme.
- Scanner excludes internal `.photoai`, `.Recycle.Bin`, and `@eaDir` folders.

## Safety model

Use **Dry Run** first when testing a production folder. PhotoAIApp excludes its own `.photoai` folders so generated audit sidecars are not recursively scanned.

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
