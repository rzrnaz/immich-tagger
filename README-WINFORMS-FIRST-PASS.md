# PhotoAI WinForms First Pass

This scaffold splits the working console prototype into three projects:

- `PhotoAIApp.Core` — shared Ollama scanner, `.photoai.json`, and Immich XMP logic.
- `PhotoAIApp` — console CLI, preserving the tested command-line workflow.
- `PhotoAIApp.Gui` — WinForms GUI for selecting an Immich-library subfolder and running the scanner.

## Apply from solution root

Expected solution root:

```powershell
C:\Users\glenc\source\repos\PhotoAIApp
```

Create the new folders/projects, copy the files into place, then build:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp

dotnet sln add .\PhotoAIApp.Core\PhotoAIApp.Core.csproj
dotnet sln add .\PhotoAIApp.Gui\PhotoAIApp.Gui.csproj
dotnet sln add .\PhotoAIApp\PhotoAIApp.csproj

dotnet clean
dotnet build
```

## Console verification

```powershell
dotnet run --no-launch-profile --project .\PhotoAIApp\PhotoAIApp.csproj -- scan "P:\photoai-test" --safety-root "P:\" --force --write-xmp
```

## GUI verification

```powershell
dotnet run --project .\PhotoAIApp.Gui\PhotoAIApp.Gui.csproj
```

Recommended first GUI values:

- Immich library root: `P:\`
- Selected folder: `P:\photoai-test`
- Force reprocess: checked
- Write XMP sidecars: checked
- Recursive: unchecked for first test
- Limit: `0` for all, or a small number for initial smoke testing

The GUI refuses to run if the selected folder is outside the configured Immich library root.


## Dry run / preview mode

The updated GUI starts with **Dry run / preview only** checked. In dry-run mode:

- No Ollama calls are made.
- No `.photoai.json` files are written.
- No `.jpg.xmp` files are written.
- No anomaly log is written.
- The app reports what it would process and what sidecars it would write.

Console preview example:

```powershell
dotnet run --no-launch-profile --project .\PhotoAIApp\PhotoAIApp.csproj -- scan "P:\photoai-test" --safety-root "P:\" --dry-run --write-xmp --limit 10
```

Real run after preview:

```powershell
dotnet run --no-launch-profile --project .\PhotoAIApp\PhotoAIApp.csproj -- scan "P:\photoai-test" --safety-root "P:\" --force --write-xmp --limit 10
```
