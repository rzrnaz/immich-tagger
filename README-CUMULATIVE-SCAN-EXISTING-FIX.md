# PhotoAI Cumulative Scan Existing Fix

This cumulative update fixes the build mismatch caused by applying the label-only `MainForm.cs` update without the matching Core scanner changes.

The build errors were because `MainForm.cs` referenced these newer Core properties:

- `OverwriteJson`
- `OverwriteXmp`
- `WouldSkipJsonSidecar`
- `WouldSkipXmpSidecar`
- `JsonWriteSkipped`
- `XmpWriteSkipped`

but the installed `PhotoAIApp.Core` files did not yet define them.

Apply this zip into:

```text
C:\Users\glenc\source\repos\PhotoAIApp
```

It updates the GUI and the matching Core/Console files together.

Then run:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp
dotnet clean
dotnet build
```

GUI labels after this update:

- `Subfolders`
- `Scan Existing`
- `Overwrite JSON`
- `Overwrite XMP`
- `Dry run / preview only`

