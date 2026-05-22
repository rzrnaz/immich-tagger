# PhotoAI Lenient Parse Fallback

This update handles occasional malformed model JSON like:

- `people_count` returned as an array instead of an integer;
- missing braces/brackets near the end of the response;
- trailing commas in nested objects.

The normal parser still runs first. If full structured parsing fails, a lenient fallback extracts the fields needed for useful XMP:

- `description`
- `short_caption`
- `tags`
- `setting`
- `quality_flags`
- `confidence`

This should turn cases that previously produced `Parse/XMP skipped: 1` into a usable analysis/XMP sidecar when the main description and tags are present in the raw response.

Apply into:

```text
C:\Users\glenc\source\repos\PhotoAIApp
```

Then:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp
dotnet clean
dotnet build
```

Retest the failed file with Scan Existing enabled, Overwrite JSON enabled, Overwrite XMP enabled, Dry Run off, Limit optional.
