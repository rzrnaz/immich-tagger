# PhotoAI GUI Layout Fix

Replace:

```text
C:\Users\glenc\source\repos\PhotoAIApp\PhotoAIApp.Gui\MainForm.cs
```

with the included `PhotoAIApp.Gui\MainForm.cs`.

This layout revision:

- increases default/minimum window size;
- uses DPI scaling;
- puts paths into a `Scan target` group;
- puts checkboxes and the limit control into an `Options` group;
- uses fixed-width columns for options so checkbox text does not clip;
- separates Run/Cancel/Open Log buttons into their own row;
- gives the log window the remaining space.

Build/test:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp
dotnet clean
dotnet build
dotnet run --project .\PhotoAIApp.Gui\PhotoAIApp.Gui.csproj
```
