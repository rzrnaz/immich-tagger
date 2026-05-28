# Immich Tagger Developer Manual

This manual is intended for developers who want to understand the Immich Tagger codebase, contribute to its development, or extend its functionality.

## Project Structure

```
.
├── PhotoAIApp.sln                 # Main Visual Studio solution
├── PhotoAIApp.Core/               # Core .NET library with scanning logic
├── PhotoAIApp.Gui/                # Windows Forms Desktop GUI
├── PhotoAIApp.Server/             # ASP.NET Core server for Docker
├── PhotoAIApp.Tests/              # Unit and integration tests  
├── docker/                        # Docker configurations
│   ├── README.md                  # Docker documentation
│   └── unraid/                    # Unraid template
│       └── immich-tagger.xml      # Unraid XML template
├── docs/                          # Documentation
│   ├── user-manual.md             # User documentation
│   └── developer-manual.md        # Developer documentation
└── RELEASE_NOTES.md               # Release notes
```

## Core Components

### PhotoAIApp.Core

This is the central library that contains all photo scanning and tagging logic.

#### Key Classes

- `ImmichTaggerSettings.cs`: Configuration model binding environment variables
- `PhotoAiScanner.cs`: Main scanning logic that interacts with Ollama models
- `PhotoAiScanOptions.cs`: Options for configuring scans  
- `PhotoAiModelProfile.cs`: Definitions for different model capabilities

#### Dependencies

- .NET 8.0 runtime
- Newtonsoft.Json for serialization
- System.Net.Http for HTTP requests to Ollama

### PhotoAIApp.Gui

The Windows Forms GUI application.

#### Features

- Visual configuration interface
- Progress tracking during scans
- Error logging and display
- Dry-run preview capability
- Settings persistence

#### Architecture

- Uses `PhotoAIApp.Core` for core functionality
- Windows Forms for UI rendering
- .NET Framework 4.8+

### PhotoAIApp.Server

ASP.NET Core server for Docker deployment.

#### Features

- Web API for starting scans
- Web UI for configuration and monitoring
- Job coordination for preventing concurrent scans
- Logging and status tracking

#### Architecture

- .NET 8.0 + ASP.NET Core
- Minimal API endpoints
- Dependency injection
- Background service for scan coordination

## Configuration

### Environment Variables

All settings can be adjusted via environment variables with the prefix `IMMICH_TAGGER__`.

Example:
```
IMMICH_TAGGER__PHOTO_ROOT=/photos
IMMICH_TAGGER__PRIMARY_OLLAMA_URL=http://localhost:11434
IMMICH_TAGGER__PRIMARY_MODEL=qwen2.5vl:7b
```

### Configuration Binding

Configuration is bound using standard .NET configuration mechanisms:
- `ImmichTaggerSettings.cs` provides strongly typed configuration
- Mapping occurs via environment variables or command-line arguments
- Settings are available through dependency injection in ASP.NET Core

## Running and Building

### Windows GUI

1. Open `PhotoAIApp.sln` in Visual Studio
2. Set `PhotoAIApp.Gui` as the startup project  
3. Build the solution
4. Run `PhotoAIApp.Gui.exe`

### Docker Server

1. Build Docker image:
   ```
   docker build -t immich-tagger:latest .
   ```

2. Run container:
   ```
   docker run --rm \
     -p 8080:8080 \
     -v /mnt/user/photos:/photos \
     -v /mnt/user/appdata/immich-tagger:/config \
     -e IMMICH_TAGGER__PRIMARY_OLLAMA_URL=http://192.168.1.8:11434 \
     -e IMMICH_TAGGER__PRIMARY_MODEL=qwen2.5vl:7b \
     immich-tagger:latest
   ```

### Testing

1. Navigate to `PhotoAIApp.Tests` directory
2. Run unit tests:
   ```
   dotnet test PhotoAIApp.Tests
   ```

## Development Guidelines

### Code Style

- Follow C# coding conventions
- Use nullable reference types throughout
- Apply synchronous patterns consistently  
- Maintain clear separation between UI and business logic

### Testing Strategy

- Unit tests for core scanning logic in `PhotoAIApp.Core`
- Integration tests for API endpoints
- GUI tests for user workflows
- End-to-end tests for full scan scenarios

### Release Considerations

- Ensure `ImmichTaggerSettings` has sensible defaults for all parameters
- All Docker-related files must be present in `docker/` directory
- Unraid template must be in sync with all parameters
- Documentation updates are required for release

## Contributing

### Submitting Changes

1. Fork the repository
2. Create your feature branch: `git checkout -b feature/your-feature`
3. Commit your changes: `git commit -am 'Add some feature'`
4. Push to the branch: `git push origin feature/your-feature`
5. Create a new Pull Request

### Reporting Issues

Please submit issues through the GitHub issue tracker with:
- Clear description of problem
- Steps to reproduce
- Expected vs actual behavior
- System/environment information

## License

This project is licensed under the MIT License. See LICENSE for details.