PhotoAIApp self-contained Windows executables

Download this release zip from:

  https://github.com/rzrnaz/PhotoAIApp/releases/download/v0.1.0/PhotoAIApp_Windows_EXE_SelfContained.zip

Inside the zip:

GUI:
  publish\PhotoAIApp-gui-win-x64-self-contained\PhotoAIApp.Gui.exe

CLI:
  publish\PhotoAIApp-cli-win-x64-self-contained\PhotoAIApp.exe

These are self-contained win-x64 publishes. You should not need Visual Studio, dotnet build, or the .NET SDK installed to run them. Extract the zip somewhere like:

  C:\Users\glenc\source\repos\PhotoAIApp\release

Then double-click:

  publish\PhotoAIApp-gui-win-x64-self-contained\PhotoAIApp.Gui.exe

If Windows SmartScreen warns because this is unsigned, choose More info -> Run anyway.

The GUI still needs network/file access to your photo folder and Ollama endpoints.
