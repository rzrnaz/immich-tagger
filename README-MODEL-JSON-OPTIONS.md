# PhotoAI Model Selector and Write JSON Option

This update adds:

- GUI `Write JSON` checkbox, default checked.
- GUI `Write XMP` checkbox, default checked.
- GUI model dropdown with `Refresh models` button that queries Ollama `/api/tags`.
- Core scanner option `WriteJson`.
- Console flag `--no-write-json` for rare debug cases.

Apply by unzipping into:

```text
C:\Users\glenc\source\repos\PhotoAIApp
```

Build:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp
dotnet clean
dotnet build
```

GUI:

```powershell
Start-Process .\PhotoAIApp.Gui\bin\Debug\net10.0-windows\PhotoAIApp.Gui.exe
```

Console preview:

```powershell
dotnet run --no-launch-profile --project .\PhotoAIApp\PhotoAIApp.csproj -- scan "P:\photoai-test" --safety-root "P:\" --dry-run --write-xmp --limit 10
```
