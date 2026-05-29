# Immich Tagger Baseline Release v1.0.0

This is the initial baseline release of the Immich Tagger project. This release establishes the foundational architecture for a Docker/Unraid version of the tool while preserving the existing Windows GUI functionality.

## What's Included

### 1. Core Architecture
- Shared .NET Core library (`PhotoAIApp.Core`) containing the scanning logic and sidecar generation
- Windows Forms GUI application (`PhotoAIApp.Gui`)
- ASP.NET Core server application (`PhotoAIApp.Server`) ready for Docker deployment
- Test suite (`PhotoAIApp.Tests`)

### 2. Key Features
- **Photo Scanning**: Recursively scans photo folders
- **Vision Model Integration**: Interface with Ollama vision models (Qwen2.5VL, MiniCPM-V)
- **Sidecar Generation**: Creates both JSON diagnostic files and Immich-compatible XMP sidecars
- **Fallback System**: Configurable primary/fallback models for robust operation
- **Cross-Platform Ready**: Windows GUI and Linux Docker server support

### 3. Release Artifacts
- Complete C# source code
- Docker configuration and Unraid template
- Documentation for users and developers
- Build scripts and project files

## Directory Structure
```
.
├── PhotoAIApp.sln                 # Solution file
├── PhotoAIApp.Core/               # Core library with scanning logic
├── PhotoAIApp.Gui/                # Windows Forms GUI
├── PhotoAIApp.Server/             # ASP.NET Core server  
├── PhotoAIApp.Tests/              # Test suite
├── docker/                        # Docker configuration
│   ├── README.md                  # Docker documentation
│   └── unraid/                    # Unraid template
│       └── immich-tagger.xml      # Unraid XML template
├── docs/                          # Documentation
│   ├── plans/
│   │   └── 2026-05-27-immich-tagger-docker-migration.md
│   ├── user-manual.md             # User guide
│   └── developer-manual.md        # Developer documentation
└── RELEASE_NOTES.md               # This file
```

## Docker/Unraid Deployment

The Immich Tagger supports running in Docker containers on Unraid systems:

### Prerequisites
- Running Ollama server accessible from the container
- Photo library mounted to `/photos`
- Persistent configuration directory mounted to `/config`

### Installation
1. Deploy using the Unraid XML template 
2. Configure environment variables for model selection and Ollama server settings

### Docker Command Example
```bash
docker run --rm \
  -p 8080:8080 \
  -v /mnt/user/photos:/photos \
  -v /mnt/user/appdata/immich-tagger:/config \
  -e IMMICH_TAGGER__PRIMARY_OLLAMA_URL=http://192.168.1.4:11434 \
  -e IMMICH_TAGGER__PRIMARY_MODEL=qwen2.5vl:7b \
  ghcr.io/rzrnaz/immich-tagger-public:v1.0.5-unraid1
```

## Getting Started

### Windows GUI
1. Build the solution using Visual Studio or `dotnet build`
2. Run `PhotoAIApp.Gui.exe`

### Docker Server  
1. Build the Docker image locally: `docker build -t immich-tagger:local .`
2. Run with appropriate volumes and environment variables

## Version History

### v1.0.0 (Baseline)
- Initial release establishing base architecture
- Windows GUI preserved and functional
- Docker/Unraid server architecture defined
- Complete documentation added