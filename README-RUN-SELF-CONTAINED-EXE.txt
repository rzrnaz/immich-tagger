PhotoAIApp self-contained Windows executables

GUI:
  PhotoAIApp-gui-win-x64-self-contained\PhotoAIApp.Gui.exe

CLI:
  PhotoAIApp-cli-win-x64-self-contained\PhotoAIApp.exe

These are self-contained win-x64 publishes. You should not need Visual Studio, dotnet build, or the .NET SDK installed to run them. Extract the zip somewhere like:

  C:\Users\glenc\source\repos\PhotoAIApp\release

Then double-click PhotoAIApp.Gui.exe. If Windows SmartScreen warns because this is unsigned, choose More info -> Run anyway.

The GUI still needs network/file access to your photo folder and Ollama endpoints.
