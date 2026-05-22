# PhotoAIApp

PhotoAIApp is a Windows/.NET tool for generating PhotoAI audit sidecars and Immich-compatible XMP sidecars for selected photo folders.

## Current release

Release page:

https://github.com/rzrnaz/PhotoAIApp/releases/tag/v0.1.0

Self-contained Windows zip:

https://github.com/rzrnaz/PhotoAIApp/releases/download/v0.1.0/PhotoAIApp_Windows_EXE_SelfContained.zip

The release zip contains:

- GUI executable: `publish\PhotoAIApp-gui-win-x64-self-contained\PhotoAIApp.Gui.exe`
- CLI executable: `publish\PhotoAIApp-cli-win-x64-self-contained\PhotoAIApp.exe`

## Current features

- WinForms GUI for selecting an Immich/library safety root and a specific folder to scan.
- Dry-run/preview mode before touching production folders.
- Writes internal `.photoai.json` audit sidecars.
- Writes Immich-style `original-file.ext.xmp` sidecars.
- Optional XMP keyword/tag fields via **Add Tags**.
- Preferred model policy:
  - Qwen PC preferred with Unraid MiniCPM-V fallback.
  - Unraid MiniCPM-V only with no fallback.
- Pause/resume for long runs, separate from Stop/cancel.
- Safe in-app folder picker that only selects folders and does not expose Windows shell delete/rename commands.

## Safety model

The selected scan folder must be under the configured library/safety root. Use **Dry Run** first when testing a production folder.

For Immich, after creating new sidecars, run metadata sidecar **Discover**, then **Sync** if needed.

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
