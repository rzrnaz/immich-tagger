# PhotoAI GUI Terminal Launch Update

This update changes:

```xml
<OutputType>WinExe</OutputType>
```

to:

```xml
<OutputType>Exe</OutputType>
```

in:

```text
PhotoAIApp.Gui\PhotoAIApp.Gui.csproj
```

Why: when a Windows GUI app is built as `WinExe`, launching it from a terminal often keeps the shell waiting until the window exits. Building as `Exe` makes it a normal console-subsystem executable that can be launched with PowerShell `Start-Process` or `start` and immediately return control to the terminal while the GUI keeps running.

Recommended launch command after build:

```powershell
Start-Process .\PhotoAIApp.Gui\bin\Debug\net10.0-windows\PhotoAIApp.Gui.exe
```

If you use `dotnet run`, the `dotnet run` process may still wait because it is a development runner. Use `Start-Process` against the built exe when you want control back immediately.

Build:

```powershell
cd C:\Users\glenc\source\repos\PhotoAIApp
dotnet build .\PhotoAIApp.Gui\PhotoAIApp.Gui.csproj
Start-Process .\PhotoAIApp.Gui\bin\Debug\net10.0-windows\PhotoAIApp.Gui.exe
```
