# PhotoAIApp Add Tags Behavior Update

This update changes the `Add Tags` option so tags are controlled independently from the sentence-style description.

## New behavior

- `dc:description` always remains the natural sentence-style description from the model.
- `tiff:ImageDescription` always remains the same sentence-style description.
- When `Add Tags` is checked, tags are written to XMP keyword/tag metadata fields:
  - `dc:subject`
  - `digiKam:TagsList`
  - `lr:HierarchicalSubject`
- When `Add Tags` is unchecked, those tag metadata fields are omitted.
- Tags no longer replace the description.

## CLI change

The old internal/CLI wording `--tags-in-description` has been removed.
Use:

```powershell
PhotoAIApp.exe scan "P:\2025\Small Folder" --safety-root "P:\" --force --write-xmp --add-tags
```

## Files changed

- `PhotoAIApp.Core/PhotoAiScanOptions.cs`
  - Renamed option from `TagsInDescription` to `AddTags`.
- `PhotoAIApp.Core/PhotoAiScanner.cs`
  - `BuildImmichXmp(...)` now keeps description fields sentence-like.
  - `addTags` only controls whether XMP tag fields are emitted.
- `PhotoAIApp.Gui/MainForm.cs`
  - The `Add Tags` checkbox now maps to `AddTags`.
- `PhotoAIApp/Program.cs`
  - CLI option changed to `--add-tags`.
- `tests/test_xmp_add_tags_behavior.py`
  - Source-level regression tests for this behavior.

## Verification performed

Installed a user-local .NET 10 SDK in `/home/gcsutton/.dotnet` and verified:

```text
dotnet build PhotoAIApp.Core/PhotoAIApp.Core.csproj --no-restore
Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build PhotoAIApp/PhotoAIApp.csproj --no-restore
Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build PhotoAIApp.Gui/PhotoAIApp.Gui.csproj -p:EnableWindowsTargeting=true --no-restore
Build succeeded. 0 Warning(s), 0 Error(s)

python3 -m pytest tests/test_xmp_add_tags_behavior.py -q
3 passed
```

Also published framework-dependent Windows x64 test builds under `publish/`:

- `publish/PhotoAIApp-cli-win-x64/PhotoAIApp.exe`
- `publish/PhotoAIApp-gui-win-x64/PhotoAIApp.Gui.exe`

These require the .NET 10 Desktop Runtime / Runtime on the Windows test machine.
