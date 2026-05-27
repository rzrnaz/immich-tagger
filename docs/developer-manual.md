# Immich Tagger Developer Manual

Immich Tagger is the go-forward product name for the former PhotoAIApp project.

This manual explains the current pieces, the target Docker/server architecture, and where developers should look when changing user-facing behavior.

## Product identity

- Product name: `Immich Tagger`
- Legacy/current repo name: `PhotoAIApp`
- Slug: `immich-tagger`
- Docker image target: `ghcr.io/rzrnaz/immich-tagger`
- Future Windows executable target: `ImmichTagger.exe`
- Container appdata target: `/mnt/user/appdata/immich-tagger`

The rename should be handled carefully. User-visible labels should move to Immich Tagger now, but project/namespace renames can be staged later to avoid breaking the existing working Windows build.

## Current solution structure

Current solution file:

```text
PhotoAIApp.sln
```

Projects:

```text
PhotoAIApp.Core/
  Shared scanner, model calls, sidecar writing, run summaries, progress models.

PhotoAIApp.Gui/
  Windows WinForms GUI.

PhotoAIApp/
  Current command-line project.

PhotoAIApp.Tests/
  Lightweight test harness.
```

Important files:

```text
PhotoAIApp.Core/PhotoAiScanner.cs
PhotoAIApp.Core/PhotoAiScanOptions.cs
PhotoAIApp.Core/RunProgress.cs
PhotoAIApp.Core/RunSummaryDocument.cs
PhotoAIApp.Core/Models.cs
PhotoAIApp.Core/PhotoAiModelProfile.cs
PhotoAIApp.Core/PhotoAiFolderSelection.cs
PhotoAIApp.Core/PhotoAiSidecarSerializer.cs
PhotoAIApp.Gui/MainForm.cs
PhotoAIApp.Gui/AdvancedSettingsForm.cs
PhotoAIApp.Gui/PhotoAiTheme.cs
PhotoAIApp/Program.cs
PhotoAIApp.Tests/Program.cs
```

## Target architecture

The Docker/server migration should move toward this structure:

```text
PhotoAIApp.Core
  Shared processing engine. No WinForms dependency.

PhotoAIApp.Gui
  Existing Windows desktop GUI. Uses Core.

PhotoAIApp.Server
  New ASP.NET Core web/API host for Docker/Unraid. Uses Core.

PhotoAIApp.Cli or current PhotoAIApp
  Command-line host for scripted/testing/scheduled runs. Uses Core.

docs
  User/developer manuals and implementation plans.

docker
  Dockerfile, compose example, Unraid XML template.
```

The most important rule: do not duplicate scanner/model/sidecar logic between GUI and server. The server and GUI should call the same core services.

## Core responsibilities

`PhotoAIApp.Core` should own:

- file discovery;
- exclusion of `.photoai`, `.Recycle.Bin`, and `@eaDir`;
- dry-run planning;
- skip/write decisions;
- model request construction;
- Ollama HTTP calls;
- primary retry logic;
- fallback endpoint logic;
- JSON audit sidecars;
- Immich XMP sidecars;
- progress snapshots;
- run summaries;
- anomaly log text;
- aggregate summary combination.

Core should not own:

- WinForms controls;
- web UI markup;
- Docker-specific path defaults except through a reusable settings type;
- Unraid XML rendering;
- secrets committed to source.

## Windows GUI responsibilities

`PhotoAIApp.Gui` owns:

- folder source text box and folder tree;
- browse buttons;
- visible field layout;
- main action buttons;
- live progress presentation;
- recent live log display;
- Advanced settings dialog;
- help buttons and tooltips;
- warm minimalist theme.

Current GUI conventions:

- Product label should become `Immich Tagger`.
- `Run Scan` label stays exact.
- `Open Log` label stays exact.
- Main log area shows only recent human-readable lines.
- Progress should include inline percent, e.g. `129 / 2210 (6%)`.
- Retry and fallback counters remain visible.
- Advanced dialog must remain DPI-safe with non-clipped labels.
- Warm minimalist theme is preferred over dark graphite/teal.

## Server/Docker responsibilities

The new server project should own:

- HTTP binding on `0.0.0.0:8080`;
- first-run config binding from environment variables;
- saved runtime settings under `/config/immich-tagger-settings.json`;
- web UI/API routes;
- one active scan job at a time;
- cancellation;
- live progress snapshot storage;
- recent log storage;
- links to persistent logs;
- status page/healthcheck;
- `/healthz` endpoint used by the Dockerfile health check;
- Docker-friendly path assumptions.

The server should not try to show a native desktop file picker. It should operate on mounted paths visible inside the container, primarily `/photos`.

## Docker runtime model

The app container should not bundle Ollama or model weights.

Runtime pieces:

```text
Immich Tagger container
  Scans files, writes sidecars, displays web UI.

Ollama server
  Runs vision models and exposes HTTP API on port 11434.

Immich
  Later discovers/syncs the generated XMP sidecars.
```

The container only needs network access to Ollama and filesystem access to mounted photo paths.

## Container publishing

The intended permanent container image is:

```text
ghcr.io/rzrnaz/immich-tagger:latest
```

The Unraid template points at that image. The GitHub Actions workflow at `.github/workflows/immich-tagger-container.yml` builds the Dockerfile and publishes branch/tag/default-branch images to GitHub Container Registry. A temporary smoke-test container created manually on Unraid should be removed after verification; users should not expect to see a permanent Unraid container until the template or compose file is installed.

## Unraid safety rules

Use Unraid mappings such as:

```text
/config -> /mnt/user/appdata/immich-tagger
/photos -> /mnt/user/photos or the relevant library share
```

Recommended media-writing identity:

```text
PUID=99
PGID=100
UMASK=000
```

Never add broad recursive chmod/chown behavior under appdata or media libraries as part of normal Immich Tagger scanning. Permission repair is a separate server-admin concern.

## Configuration cross-reference

### Product name

- User manual label: `Immich Tagger`
- README title: `Immich Tagger`
- GUI title target: `PhotoAIApp.Gui/MainForm.cs`
- Server title target: future `PhotoAIApp.Server` UI files
- Docker slug: `immich-tagger`

### Source folder

- User setting: source/photo folder
- Docker default: `/photos`
- Core option: `PhotoAiScanOptions.FolderPath`
- GUI source: `MainForm` selected folder/folder tree
- CLI source: `PhotoAIApp/Program.cs` `scan <folder-path>`
- Server target: request/settings model field, likely `FolderPath`

### Safety root

- User setting: hidden/server-config or CLI safety root
- Core option: `PhotoAiScanOptions.SafetyRootPath`
- GUI convention: no separate Immich library root field in main GUI
- CLI option: `--safety-root`
- Docker target: usually `/photos`

### Recursive / Subfolders

- User label: `Subfolders`
- Core option: `PhotoAiScanOptions.Recursive`
- GUI field: `_recursiveCheckBox`
- CLI option: `--recursive`
- Docker/server default: enabled

### Scan Existing / Force

- User label: `Scan Existing`
- Core option: `PhotoAiScanOptions.Force`
- GUI field: `_forceCheckBox`
- CLI option: `--force`

### Write JSON

- User label: `Write JSON` or implied audit JSON
- Core option: `PhotoAiScanOptions.WriteJson`
- CLI current behavior: `true`
- Server target: visible setting, default enabled

### Write XMP

- User label: `Write XMP`
- Core option: `PhotoAiScanOptions.WriteXmp`
- CLI option: `--write-xmp`
- Server target: visible setting, default enabled

### Overwrite sidecars

- User label: `Overwrite XMP+` / `Overwrite Sidecars`
- Core options: `OverwriteJson`, `OverwriteXmp`, `OverwriteSidecars`
- GUI field: `_overwriteSidecarsCheckBox`
- CLI options: `--overwrite-sidecars`, legacy `--overwrite-json`, `--overwrite-xmp`
- Server target: visible setting

### Add Tags

- User direction: tags are primary and should be default/on
- Core option: `PhotoAiScanOptions.AddTags`
- GUI legacy/current field: previously `_addTagsCheckBox`; recent direction makes tags always true
- CLI option: `--add-tags`
- Server target: default enabled, optionally configurable later

### Dry Run

- User label: `Dry Run`
- Core option: `PhotoAiScanOptions.DryRun`
- GUI field: `_dryRunCheckBox`
- CLI option: `--dry-run`
- Server target: dry-run button and/or default safety mode

### Limit

- User label: `Limit`
- Core option: `PhotoAiScanOptions.Limit`
- GUI field: `_limitNumeric`
- CLI option: `--limit <number>`
- Server target: numeric field
- Important behavior: limit should count non-skipped images, not raw files, where current code supports that direction.

### Primary Ollama URL

- User label: primary Ollama host/IP/URL
- Core option: `PhotoAiScanOptions.OllamaBaseUrl`
- Defaults: `PhotoAiDefaults.QwenPcOllamaBaseUrl`, Docker template may use `http://192.168.1.8:11434`
- GUI Advanced dialog: primary URL/port controls
- Server target env var: `IMMICH_TAGGER__PRIMARY_OLLAMA_URL`

### Primary model

- User label: primary model
- Core option: `PhotoAiScanOptions.Model`
- Known high-quality default: `qwen2.5vl:7b`
- GUI Advanced dialog: model dropdown/manual entry
- Server target env var: `IMMICH_TAGGER__PRIMARY_MODEL`

### Max Image Size

- User label must be exactly: `Max Image Size`
- Core option: `PhotoAiScanOptions.MaxImageDimensionPixels`
- Meaning: `0 = original/full-res`
- Server target env var: `IMMICH_TAGGER__MAX_IMAGE_SIZE`

### Fallback settings

- Core options:
  - `PhotoAiScanOptions.ModelPreference`
  - `PhotoAiScanOptions.FallbackOllamaBaseUrl`
  - `PhotoAiScanOptions.FallbackModel`
  - `PhotoAiScanOptions.FallbackMaxImageDimensionPixels`
- Known fallback: `minicpm-v:latest`
- Important mode: `PrimaryOnly` is distinct from `UnraidMiniCpmOnly`
- Server env vars:
  - `IMMICH_TAGGER__FALLBACK_ENABLED`
  - `IMMICH_TAGGER__FALLBACK_OLLAMA_URL`
  - `IMMICH_TAGGER__FALLBACK_MODEL`
  - `IMMICH_TAGGER__FALLBACK_MAX_IMAGE_SIZE`

### Immich sidecar sync

- User label: `Sync Immich`
- GUI current direction: optional checkbox after live scan
- Behavior: Discover new sidecars, wait for idle, then Sync existing sidecars
- Secrets/config: use a local ignored file or `/config` secret, never commit API keys
- API endpoint known from earlier work: `PUT /api/jobs/sidecar` with `x-api-key`

## Logs and diagnostics

Keep deep telemetry in:

- anomaly logs;
- JSON sidecars;
- final summaries.

Avoid flooding live UI logs with every low-level detail.

Important diagnostics include:

- model name;
- endpoint URL;
- resize mode and dimensions;
- Ollama error response;
- retry counters;
- fallback counters;
- sidecar output paths;
- elapsed time;
- average seconds per processed photo.

## Build commands

Windows development:

```powershell
dotnet run --project .\PhotoAIApp.Tests\PhotoAIApp.Tests.csproj
dotnet build .\PhotoAIApp.sln
```

Windows GUI publish from Windows:

```powershell
dotnet publish .\PhotoAIApp.Gui\PhotoAIApp.Gui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\PhotoAIApp-gui-win-x64-self-contained
```

Windows GUI publish from Linux:

```bash
dotnet publish ./PhotoAIApp.Gui/PhotoAIApp.Gui.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/PhotoAIApp-gui-win-x64-self-contained
```

Docker server build target once implemented:

```bash
docker build -t immich-tagger:local .
```

## Test limitations in the current Hermes environment

The current Hermes Linux environment may not have `dotnet` installed. If `dotnet` is unavailable, syntax/build verification must be performed later on the Windows development machine or a Linux build environment with the .NET SDK installed.

## Release notes

When the Docker/server version reaches a breakpoint:

- commit source/docs/support files;
- keep large binaries out of git;
- create a normal GitHub release from `main`;
- upload Windows ZIP/EXE assets if still desired;
- publish Docker image;
- include Unraid XML template;
- download/hash-verify published assets before reporting success.
