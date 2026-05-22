# PhotoAI Tags in Description Update

This update makes tag enrichment a fallback/search aid instead of a large tag dump:

- enriched tags are capped at 10;
- GUI adds `Tags in Description` checkbox;
- when checked, XMP description fields use the comma-separated tag list instead of the long narrative description;
- when unchecked, existing description behavior remains unchanged;
- XMP tag fields are still written either way.

Apply into `C:\Users\glenc\source\repos\PhotoAIApp`, then run:

```powershell
Stop-Process -Name PhotoAIApp.Gui -ErrorAction SilentlyContinue
dotnet clean
dotnet build
```
