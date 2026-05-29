# Immich Tagger Docker Deployment

This is the Docker deployment guide for the Immich Tagger ASP.NET Core server/container.

## Overview

Immich Tagger can run as a Docker container on Linux systems including Unraid. This lets you process photo libraries without tying up a Windows PC while keeping the same XMP-first workflow used by the validated Windows baseline.

## Prerequisites

- Docker installed on your system
- A running Ollama server accessible from the container
- Photo library mounted to a filesystem accessible by the container
- Docker volume permissions configured appropriately for both reads and sidecar writes

## Quick Start

```bash
# Build the image locally
docker build -t immich-tagger:local .

# Run with required volume mappings
docker run --rm \
  -p 8080:8080 \
  -v /path/to/photos:/photos \
  -v /path/to/config:/config \
  -e IMMICH_TAGGER__PRIMARY_OLLAMA_URL=http://192.168.1.4:11434 \
  -e IMMICH_TAGGER__PRIMARY_MODEL=qwen2.5vl:7b \
  -e IMMICH_TAGGER__FALLBACK_ENABLED=false \
  immich-tagger:local
```

For live runs, `/photos` must be mounted **read-write** because the container writes `*.jpg.xmp` sidecars and `.photoai` anomaly logs next to the scanned media. A read-only `/photos` mount is only suitable for folder browsing and dry-run previews. The server now performs a live-run write preflight and will block the run early with a clear error if the selected folders or their `.photoai` log directories are not writable.

## Environment Variables

All configuration is handled via environment variables with the prefix `IMMICH_TAGGER__`:

- `IMMICH_TAGGER__PHOTO_ROOT` - Container directory for photos (default: `/photos`)
- `IMMICH_TAGGER__CONFIG_ROOT` - Container directory for config/logs (default: `/config`)
- `IMMICH_TAGGER__LOG_ROOT` - Container directory for logs (default: `/config/logs`)
- `IMMICH_TAGGER__PRIMARY_OLLAMA_URL` - URL of primary Ollama server (default/example: `http://192.168.1.4:11434` for the validated Qwen 7B host)
- `IMMICH_TAGGER__PRIMARY_MODEL` - Primary vision model name (default: `qwen2.5vl:7b`)
- `IMMICH_TAGGER__MAX_IMAGE_SIZE` - Resize images before sending (0 = original/full-res, default: 0)
- `IMMICH_TAGGER__FALLBACK_ENABLED` - Enable fallback model if primary fails (default: true)
- `IMMICH_TAGGER__FALLBACK_OLLAMA_URL` - URL of fallback Ollama server (example: `http://192.168.1.8:11434` for MiniCPM-V on Unraid)
- `IMMICH_TAGGER__FALLBACK_MODEL` - Fallback model name (default: `minicpm-v:latest`)
- `IMMICH_TAGGER__FALLBACK_MAX_IMAGE_SIZE` - Resize before sending to fallback model (0 = original/full-res, default: 0)
- `IMMICH_TAGGER__DRY_RUN_DEFAULT` - Default to dry run mode (default: true)
- `IMMICH_TAGGER__WRITE_JSON` - Enable optional JSON sidecar generation (default: false)
- `IMMICH_TAGGER__WRITE_XMP` - Enable XMP sidecar generation (default: true)
- `IMMICH_TAGGER__ADD_TAGS` - Add AI-generated tags to XMP (default: true)
- `IMMICH_TAGGER__OVERWRITE_SIDECARS` - Overwrite existing sidecars (default: false)
- `IMMICH_TAGGER__SYNC_IMMICH` - Trigger Immich sidecar Discover then Sync after a successful live run (default: false)
- `IMMICH_TAGGER__IMMICH_BASE_URL` - Immich server base URL used for Discover/Sync (default app setting: `http://192.168.1.8:2283`)
- `IMMICH_TAGGER__IMMICH_API_KEY` - Immich API key for optional sidecar job requests

## Volume Mounts

- `/photos` - Photo library directory. Use **read-write** for live runs because XMP sidecars and `.photoai` anomaly logs are written beside the photos.
- `/config` - Configuration and server logs directory (read-write)

## Ports

- `8080` - Web UI for managing scans and viewing logs

## Unraid Installation

Use the included Unraid XML template to deploy. New builds should use the pinned published image tag `ghcr.io/rzrnaz/immich-tagger-public:v1.0.6-unraid1` rather than `latest`.

1. From Unraid's "Apps" tab
2. Click "Add Docker"
3. Click "Click here to add a new container"
4. Copy the contents of `docker/unraid/immich-tagger.xml`
5. Replace default settings as needed
6. Save and launch

## Usage

Once the container is running, access the web UI at:

```text
http://<hostip>:8080
```

From the UI:

1. Browse folders under `/photos` and build a selected-folder list.
2. Run a Dry Run to preview what would be processed across the selected folders.
3. Run a Live Scan to write XMP sidecars.
4. Use Pause / Resume / Stop during long runs.
5. View progress, summaries, and anomaly logs in real time.
6. Optionally enable Immich Discover/Sync settings so a completed live run can refresh Immich sidecar metadata automatically.

## Security

For production deployments:

- Run the container with a non-root user
- Ensure volume permissions are set correctly for Unraid
- Use HTTPS termination if running exposed publicly
- Keep any Immich API key in your config/appdata path or secrets manager, not in source control

## Troubleshooting

### Connection Issues

Ensure the chosen Ollama server is accessible from the container and actually advertises the model tag you configured:

```bash
# From container, test connectivity:
curl http://192.168.1.4:11434/api/tags
```

### Permission Issues

Check that the container user (PUID/PGID) has appropriate permissions to read photos and write both sidecars and logs. Default Unraid settings:

- PUID: 99 (nobody)
- PGID: 100 (users)
- UMASK: 000 (permissive)

If dry run succeeds but live run fails immediately on `.jpg.xmp` or `/photos/.photoai` writes, treat that as a bind-mount/runtime permission problem rather than an Ollama/model problem. Newer server builds preflight these writes before the run starts, so a read-only or mismatched-permission mount should be rejected up front with a `Live scan blocked:` message.

### Model Issues

Verify the requested model is available on the Ollama endpoint you configured:

```bash
# From the Ollama host:
ollama list
```
