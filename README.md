# PhotoAIApp

PhotoAIApp is a Windows/.NET tool for generating PhotoAI audit sidecars and Immich-compatible XMP sidecars for selected photo folders.

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
