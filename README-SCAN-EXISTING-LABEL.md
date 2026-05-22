# PhotoAI Scan Existing Label Update

This is a GUI-label-only update.

The checkbox formerly displayed as:

```text
Overwrite
```

now displays as:

```text
Scan Existing
```

Behavior is unchanged: this controls whether images that already have a `.photoai.json` sidecar are scanned again.

- `Scan Existing` off: existing `.photoai.json` skips the whole image.
- `Scan Existing` on: existing `.photoai.json` does not skip the image; the image is analyzed again.

This differentiates it from:

- `Overwrite JSON`
- `Overwrite XMP`

Apply into:

```text
C:\Users\glenc\source\repos\PhotoAIApp
```

Build:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp
dotnet build
```
