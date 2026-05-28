# Immich Tagger Docker/Unraid Notes

Immich Tagger is planned as a Docker/Unraid server app rather than a containerized Windows desktop GUI.

The container should:

- expose a web UI on port `8080`;
- mount the photo library at `/photos`;
- persist configuration and logs under `/config`;
- call an external Ollama server over HTTP;
- write JSON and Immich-compatible XMP sidecars through the mounted `/photos` path;
- use Unraid-friendly UID/GID/UMASK values.

## Planned image

```text
ghcr.io/rzrnaz/immich-tagger:latest
```

## Planned paths

```text
/photos  -> photo library or Immich external-library share
/config  -> persistent appdata/config/logs
```

Recommended Unraid host mappings:

```text
/mnt/user/photos                  -> /photos
/mnt/user/appdata/immich-tagger   -> /config
```

If the Immich external library lives somewhere else, map that host path to `/photos` instead.

## Planned ports

```text
8080/tcp -> web UI
```

## Planned environment variables

```text
PUID=99
PGID=100
UMASK=000
TZ=America/Phoenix
IMMICH_TAGGER__PHOTO_ROOT=/photos
IMMICH_TAGGER__CONFIG_ROOT=/config
IMMICH_TAGGER__LOG_ROOT=/config/logs
IMMICH_TAGGER__PRIMARY_OLLAMA_URL=http://192.168.1.8:11434
IMMICH_TAGGER__PRIMARY_MODEL=qwen2.5vl:7b
IMMICH_TAGGER__MAX_IMAGE_SIZE=0
IMMICH_TAGGER__FALLBACK_ENABLED=true
IMMICH_TAGGER__FALLBACK_OLLAMA_URL=http://192.168.1.8:11434
IMMICH_TAGGER__FALLBACK_MODEL=minicpm-v:latest
IMMICH_TAGGER__FALLBACK_MAX_IMAGE_SIZE=0
IMMICH_TAGGER__DRY_RUN_DEFAULT=true
IMMICH_TAGGER__WRITE_JSON=false
IMMICH_TAGGER__WRITE_XMP=true
IMMICH_TAGGER__ADD_TAGS=true
IMMICH_TAGGER__OVERWRITE_SIDECARS=false
```

## Important design note

Do not run the current WinForms GUI inside Docker. The Docker version should be a server/web app that reuses the shared scanner engine.

The Windows GUI can remain available as a separate desktop client.
