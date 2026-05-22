# PhotoAI GUI Layout Fix 2

Replace:

```text
C:\Users\glenc\source\repos\PhotoAIApp\PhotoAIApp.Gui\MainForm.cs
```

with the included `PhotoAIApp.Gui\MainForm.cs`.

This revision gives more room to:

- `Immich library root:` label
- `Selected folder:` label
- `Subfolders` checkbox
- `Dry run / preview only` checkbox
- button row, with `Open log` as a shorter label

It also uses a wrapping options row so text should remain visible on scaled displays.

Build/test:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp
dotnet clean
dotnet build
dotnet run --project .\PhotoAIApp.Gui\PhotoAIApp.Gui.csproj
```


Label standardization:

- The checkbox formerly displayed as `Recursive` now displays as `Subfolders`.
- The checkbox formerly displayed as `Force reprocess` now displays as `Overwrite`.
- Internal code names are unchanged in this layout-only update; behavior is unchanged.
