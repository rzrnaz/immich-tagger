# PhotoAI Options Layout Fix

This is a GUI layout-only fix for the options area.

It changes the Options section from one crowded row into two rows:

Row 1:

- Subfolders
- Scan Existing
- Overwrite JSON
- Overwrite XMP

Row 2:

- Dry run / preview
- Limit, 0 = all
- numeric limit box

This gives the overwrite labels enough room and stops the limit spinner from being crushed under the Subfolders checkbox.

Apply into:

```text
C:\Users\glenc\source\repos\PhotoAIApp
```

Build:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp
dotnet build
```
