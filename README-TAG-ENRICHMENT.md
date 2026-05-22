# PhotoAI Tag Enrichment Update

This update enriches model tags before writing JSON/XMP.

It keeps model-provided tags, then adds useful searchable terms from the description and short caption:

- objects, animals, places, rooms, vehicles, activities;
- visible quoted text/brands/signs like `Valvoline`;
- selected known phrases like `covered car` or `license plate`.

It filters generic noise like image, photo, object, item, thing, scene, and environment.

Apply into `C:\Users\glenc\source\repos\PhotoAIApp`, then run:

```powershell
Stop-Process -Name PhotoAIApp.Gui -ErrorAction SilentlyContinue
dotnet clean
dotnet build
```
