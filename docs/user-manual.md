# Immich Tagger User Manual

Welcome to the Immich Tagger! This tool helps you automatically tag your photos with AI-powered descriptions and keywords using Vision Language Models (VLMs) that work with Immich.

## Overview

Immich Tagger scans your photo library, sends images to an Ollama vision model, and creates Immich-compatible sidecars. It supports both a Windows GUI version and a Linux Docker version for Unraid environments.

The tool creates both JSON diagnostic files (for troubleshooting) and XMP sidecars (for Immich integration) next to your photos.

## What Gets Created

### XMP Sidecar Files
- File extension: `.jpg.xmp` 
- Contains AI-generated tags/keywords for Immich
- Follows Immich's supported format

### JSON Sidecar Files  
- File extension: `.photoai.json`
- Contains detailed AI responses for troubleshooting
- Option to disable for production use

## Getting Started

### Windows GUI Version

1. **Prerequisites**
   - Windows 10+  
   - .NET 8.0 runtime
   - Running Ollama server on the same network

2. **Usage**
   - Launch `PhotoAIApp.Gui.exe`
   - Configure the settings:
     - Set the photo library root path
     - Configure Ollama server URL
     - Select the model to use
   - Choose a folder to scan
   - Start a Dry Run to preview what would be processed
   - Start a Live Scan to actually process and tag photos

### Docker/Unraid Version

1. **Prerequisites**
   - Running Ollama server accessible from Docker container
   - Photos in an accessible filesystem path
   - Docker-compatible environment (like Unraid)

2. **Configuration**
   - Map photo library to `/photos`
   - Map config directory to `/config`
   - Configure environment variables for Ollama settings
   - Access the web UI at `http://<host>:8080`

## Settings Reference

| Setting | Description | Default |
|---------|-------------|---------|
| Photo Root | Directory where photos are located | `/photos` |
| Config Root | Directory for app config/logs | `/config` |
| Log Root | Directory for log files | `/config/logs` |
| Primary Ollama URL | URL of the primary Ollama server | `http://192.168.1.4:11434` |
| Primary Model | Name of the primary vision model | `qwen2.5vl:7b` |
| Max Image Size | Resize images before sending to model (0 = no resize) | `0` |
| Fallback Enabled | Enable fallback model if primary fails | `true` |
| Fallback Ollama URL | URL of the fallback Ollama server | `http://192.168.1.8:11434` |
| Fallback Model | Name of the fallback vision model | `minicpm-v:latest` |
| Dry Run Default | Default to dry-run mode for safety | `true` |
| Write JSON | Enable JSON sidecar creation | `false` |
| Write XMP | Enable XMP sidecar creation | `true` |
| Add Tags | Enable AI-generated tags in XMP | `true` |

## Safety Features

### Dry Runs
Before processing photos, you can perform a dry run to see exactly what would be tagged without modifying files.

### Fallback Models
If the primary model fails, the tool will automatically attempt a fallback model that provides similar (but potentially lower quality) results.

### Overwrite Protection  
By default, existing sidecars are left unchanged. You can enable overwrite mode to regenerate all sidecars.

## Troubleshooting

### Common Issues

1. **Connection to Ollama Failed**
   - Ensure the Ollama server is running
   - Verify the URL in settings is correct
   - Confirm network connectivity between applications

2. **Missing Model**
   - Pull the required model with `ollama pull qwen2.5vl:7b`
   - Check model name is exactly correct in settings

3. **Permissions Issues**
   - Ensure the tool has read/write privileges to photo library
   - Check Unraid media permissions match the container user/group settings

## Example Workflow  

Here's a typical workflow when using Immich with the Tagger:

1. **Scan Photos**
   - Launch Immich Tagger and select a photo subfolder
   - Run a Dry Run to see what would be processed
   
2. **Tag Photos**
   - Run a Live Scan to generate sidecars
   - Check that `.jpg.xmp` files were created alongside photos

3. **Import into Immich**
   - In Immich, run a Discover scan to detect the new sidecars
   - Run Sync to import the tags into your photo library

## Support

For additional help, visit the GitHub repository at: https://github.com/rzrnaz/PhotoAIApp