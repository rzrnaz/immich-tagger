# Immich Tagger Docker Deployment

This is the Docker deployment documentation for the Immich Tagger project.

## Overview

The Immich Tagger can run as a Docker container on Linux systems including Unraid. This setup allows you to process photo libraries without tying up a Windows PC, making it perfect for automated tagging workflows.

## Prerequisites

- Docker installed on your system
- A running Ollama server accessible from the container 
- Photo library mounted to a filesystem accessible by the container
- Docker volume permissions configured appropriately

## Quick Start

```bash
# Build the image
docker build -t immich-tagger:latest .

# Run with required volume mappings
docker run --rm \
  -p 8080:8080 \
  -v /path/to/photos:/photos \
  -v /path/to/config:/config \
  -e IMMICH_TAGGER__PRIMARY_OLLAMA_URL=http://192.168.1.8:11434 \
  -e IMMICH_TAGGER__PRIMARY_MODEL=qwen2.5vl:7b \
  immich-tagger:latest
```

## Environment Variables

All configuration is handled via environment variables with the prefix `IMMICH_TAGGER__`:

- `IMMICH_TAGGER__PHOTO_ROOT` - Container directory for photos (default: `/photos`)
- `IMMICH_TAGGER__CONFIG_ROOT` - Container directory for config/logs (default: `/config`)
- `IMMICH_TAGGER__LOG_ROOT` - Container directory for logs (default: `/config/logs`)
- `IMMICH_TAGGER__PRIMARY_OLLAMA_URL` - URL of primary Ollama server (default: `http://192.168.1.8:11434`)
- `IMMICH_TAGGER__PRIMARY_MODEL` - Primary vision model name (default: `qwen2.5vl:7b`)
- `IMMICH_TAGGER__MAX_IMAGE_SIZE` - Resize images before sending (0 = no resize, default: 0)
- `IMMICH_TAGGER__FALLBACK_ENABLED` - Enable fallback model if primary fails (default: true)
- `IMMICH_TAGGER__FALLBACK_OLLAMA_URL` - URL of fallback Ollama server (default: same as primary)
- `IMMICH_TAGGER__FALLBACK_MODEL` - Fallback model name (default: `minicpm-v:latest`)
- `IMMICH_TAGGER__DRY_RUN_DEFAULT` - Default to dry run mode (default: true)
- `IMMICH_TAGGER__WRITE_JSON` - Enable JSON sidecar generation (default: false)
- `IMMICH_TAGGER__WRITE_XMP` - Enable XMP sidecar generation (default: true)  
- `IMMICH_TAGGER__ADD_TAGS` - Add AI-generated tags to XMP (default: true)
- `IMMICH_TAGGER__OVERWRITE_SIDECARS` - Overwrite existing sidecars (default: false)

## Volume Mounts

- `/photos` - Photo library directory (read-only for container)
- `/config` - Configuration and logs directory (read-write for container)

## Ports 

- `8080` - Web UI for managing scans and viewing logs

## Unraid Installation

Use the included Unraid XML template to deploy:

1. From Unraid's "Apps" tab
2. Click "Add Docker"  
3. Click "Click here to add a new container"
4. Copy the contents of `docker/unraid/immich-tagger.xml`
5. Replace default settings as needed
6. Save and launch

## Usage

Once the container is running, access the web UI at:
```
http://<hostip>:8080
```

From the UI:
1. Select a photo folder under `/photos`
2. Run a Dry Run to preview what would be processed  
3. Run a Live Scan to actually tag photos
4. View logs and progress in real-time

## Security

For production deployments:
- Run container with non-root user
- Ensure volume permissions are set correctly for Unraid
- Use HTTPS termination if running exposed publicly

## Troubleshooting

### Connection Issues

Ensure Ollama server is accessible from container:
```
# From container, test connectivity:
curl http://192.168.1.8:11434/api/tags
```

### Permission Issues

Check that the container user (PUID/PGID) has appropriate permissions to read photos and write logs. Default Unraid settings:
- PUID: 99 (nobody)
- PGID: 100 (users)  
- UMASK: 000 (permissive)

### Model Issues

Verify the requested model is available in Ollama:
```
# From Ollama host:
ollama list
```