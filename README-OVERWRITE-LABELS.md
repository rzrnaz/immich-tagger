# PhotoAI Overwrite Label/Behavior Update

This update changes GUI option labels to:

- `Overwrite`
- `Overwrite JSON`
- `Overwrite XMP`

Behavior:

- `Overwrite`: re-analyze photos that already have `.photoai.json`. If off, existing `.photoai.json` skips the whole image.
- `Overwrite JSON`: when an image is processed, allow replacing an existing `.photoai.json`; if off, existing JSON is preserved.
- `Overwrite XMP`: when an image is processed and XMP is generated, allow replacing an existing `.jpg.xmp`; if off, existing XMP is preserved.

The GUI always plans/writes JSON and XMP outputs for processed images, subject to these overwrite protections. Dry run still writes nothing.

Apply into:

```text
C:\Users\glenc\source\repos\PhotoAIApp
```

Build:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp
dotnet clean
dotnet build
```
