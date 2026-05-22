# PhotoAI Add Tags Label and Pretty XMP

This update:

- renames the GUI checkbox from `Tags in Description` to `Add Tags`;
- writes XMP with normal line breaks/indentation instead of one long line;
- keeps the same XML elements and namespaces, so Immich should ingest it the same way.

`Add Tags` behavior is unchanged: when checked, the XMP description fields use the compact comma-separated tag/noun list instead of the long narrative description.

Apply into:

```text
C:\Users\glenc\source\repos\PhotoAIApp
```

Then:

```powershell
Stop-Process -Name PhotoAIApp.Gui -ErrorAction SilentlyContinue
dotnet clean
dotnet build
```
