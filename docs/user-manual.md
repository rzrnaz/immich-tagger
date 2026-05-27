# Immich Tagger User Manual

Immich Tagger is the new name and direction for the former PhotoAIApp project.

Tagline:

```text
AI-powered tags, descriptions, and XMP sidecars for Immich photo libraries.
```

Immich Tagger scans folders of photos, sends each selected image to an Ollama vision model, and writes metadata sidecars that Immich can discover and sync. The larger value is automated tagging, but descriptions/captions are also generated and written when available.

## What Immich Tagger does

Immich Tagger is designed for photo libraries stored on a server, especially Unraid-hosted Immich external libraries.

It can:

- scan one folder or multiple folders of photos;
- optionally include subfolders;
- run a dry-run preview before changing files;
- generate AI descriptions/captions;
- generate AI tags/keywords;
- write Immich-friendly `.xmp` sidecars next to the original image files;
- write internal JSON audit sidecars for troubleshooting and review;
- preserve existing/protected descriptions unless overwrite behavior is enabled;
- use a primary Ollama model and optional fallback model/server;
- show progress, retry/fallback counters, summaries, and logs.

## What Immich Tagger does not do

Immich Tagger is not a replacement for Immich.

It does not:

- store photos;
- replace the Immich database;
- directly edit original image files;
- require photos to be uploaded to a cloud service;
- bundle Ollama or AI models inside the app container;
- recursively repair media-library permissions;
- automatically expose itself safely to the public internet.

## How the metadata flow works

1. Immich Tagger reads an image from the selected folder.
2. It sends image content to an Ollama vision model.
3. The model returns structured metadata such as description and tags.
4. Immich Tagger writes:
   - an XMP sidecar beside the image for Immich;
   - a JSON sidecar under the `.photoai` audit area for review/debugging.
5. Immich then needs to discover/sync the sidecar metadata.

## Files created

For an image like:

```text
/photos/Family/2024/Picnic/IMG_1234.jpg
```

Immich Tagger may create an XMP sidecar like:

```text
/photos/Family/2024/Picnic/IMG_1234.jpg.xmp
```

It also creates internal audit/log output under `.photoai`, for example:

```text
/photos/Family/2024/Picnic/.photoai/
```

The scanner must never scan its own `.photoai` folders. It also excludes `.Recycle.Bin` and `@eaDir` folders.

## Tags versus descriptions

Tags are the primary value of Immich Tagger.

Tags help with:

- search;
- grouping similar photos;
- finding objects, locations, animals, activities, vehicles, scenes, and events;
- improving the usefulness of large Immich libraries.

Descriptions/captions are also useful, but the main product direction is now tag-centered.

## Recommended model approach

Known-good high-quality path from prior testing:

```text
Model: qwen2.5vl:7b
Max Image Size: 0 / original full resolution
Hardware: Windows RTX 5080-class GPU
```

Known Unraid compatibility/fallback path:

```text
Model: minicpm-v:latest
Max Image Size: 0 / original full resolution
Hardware: Unraid GPU path
```

Important note:

```text
Avoid qwen2.5vl:3b-q4_K_M unless explicitly testing/diagnosing it.
```

That tag previously showed Ollama runner instability and should not be used as the normal safe/default option.

## Main settings

### Photo folder / source directory

The folder to scan.

In Docker/Unraid, the container should see the photo library at:

```text
/photos
```

Example host-to-container mapping:

```text
Host path:      /mnt/user/photos
Container path: /photos
```

Then a real folder might be entered in the app as:

```text
/photos/Family/2024/Picnic
```

### Config directory

Persistent app configuration should live at:

```text
/config
```

Recommended Unraid host mapping:

```text
Host path:      /mnt/user/appdata/immich-tagger
Container path: /config
```

When settings are changed from the web UI, Immich Tagger writes them to:

```text
/config/immich-tagger-settings.json
```

The Unraid template/environment variables are the first-run defaults. Saved web UI settings are then reloaded from `/config` after container restarts.

### Logs

Docker/server logs should persist under:

```text
/config/logs
```

Run-specific scan/anomaly logs may also be created in the selected photo tree's `.photoai` folder depending on the scanner path.

### Recursive / Subfolders

When enabled, Immich Tagger scans subfolders under the selected folder.

Recommended default:

```text
enabled
```

### Dry Run

Dry Run previews what would happen without calling Ollama and without writing sidecars.

Recommended default:

```text
enabled
```

Use Dry Run before a first scan of any new production folder.

### Scan Existing / Force

Controls whether images with existing Immich Tagger/PhotoAI JSON sidecars can be considered for reprocessing.

Typical behavior:

- enabled: existing JSON does not automatically block reprocessing;
- disabled: existing JSON means the image is skipped.

### Overwrite XMP+ / Overwrite Sidecars

Controls whether existing JSON and XMP sidecars are regenerated.

For production use, be careful with overwrite behavior. If existing sidecars contain protected descriptions or manually curated metadata, test on a small folder first.

### Add Tags

The Docker/server direction treats tags as always-on by default because tags are the primary product value.

### Write JSON

Writes internal JSON sidecars for review/debugging.

Recommended default:

```text
enabled
```

### Write XMP

Writes Immich-compatible XMP sidecars next to images.

Recommended default:

```text
enabled
```

### Limit

Optional maximum number of non-skipped images to process.

Use this for testing:

```text
5
10
25
```

Use `0` or blank for no limit.

### Primary Ollama URL

The main Ollama server address.

Examples:

```text
http://192.168.1.8:11434
http://host.docker.internal:11434
http://192.168.1.50:11434
```

The app container does not need to run on the same machine as Ollama. It only needs network access to the Ollama HTTP API.

### Primary Model

The main vision model tag.

Recommended high-quality model:

```text
qwen2.5vl:7b
```

### Max Image Size

Controls image resize before sending to the model.

Important value:

```text
0 = original/full resolution
```

Other examples:

```text
1440 = resize longest edge to 1440px
2048 = resize longest edge to 2048px
```

### Fallback enabled

If enabled, Immich Tagger may try a fallback Ollama endpoint/model after primary failures.

Useful when:

- the main model/server is unavailable;
- a transient Ollama request fails;
- an Unraid compatibility model is available.

### Fallback Ollama URL

Example:

```text
http://192.168.1.8:11434
```

### Fallback Model

Known useful fallback:

```text
minicpm-v:latest
```

## Docker/Unraid settings

Recommended Unraid template values:

```text
Container name: Immich-Tagger
Repository: ghcr.io/rzrnaz/immich-tagger:latest
Web UI port: 8080
/photos: /mnt/user/photos or the relevant Immich external-library share
/config: /mnt/user/appdata/immich-tagger
PUID: 99
PGID: 100
UMASK: 000
TZ: America/Phoenix or your local timezone
```

For this user's Unraid media policy, sidecars should be written with normal container media ownership/permissions using:

```text
PUID=99
PGID=100
UMASK=000
```

Do not use Immich Tagger as a general permission-repair tool.

It is normal not to see an Immich Tagger container on Unraid until you install the Unraid template or run a compose/container instance. Development smoke-test containers are temporary and should be removed after verification. The intended permanent image name is:

```text
ghcr.io/rzrnaz/immich-tagger:latest
```

## Example: safely process one Unraid subdirectory

Assume the host has a photo share path:

```text
/mnt/user/photos/Family/2024/Picnic
```

And the Docker template maps:

```text
Host path:      /mnt/user/photos
Container path: /photos
```

Inside Immich Tagger, the folder is:

```text
/photos/Family/2024/Picnic
```

Recommended first run:

1. Open the Immich Tagger web UI.
2. Enter source folder:
   ```text
   /photos/Family/2024/Picnic
   ```
3. Enable:
   ```text
   Dry Run
   Recursive if desired
   Write JSON
   Write XMP
   Add Tags / tag output
   ```
4. Set a small limit if testing:
   ```text
   10
   ```
5. Run Dry Run.
6. Review the would-process count.
7. Disable Dry Run.
8. Keep the limit small for the first live test.
9. Run Scan.
10. Confirm sidecars were created next to images.
11. Review logs and JSON diagnostics.
12. If satisfied, increase/remove the limit and run the remaining folder.

## Optional: back up one subdirectory before testing

If you want a simple pre-run safety copy on Unraid, use a separate backup location and preserve attributes.

Example concept:

```bash
mkdir -p /mnt/user/photo-test-backups/Family-2024-Picnic
rsync -a --info=progress2 \
  /mnt/user/photos/Family/2024/Picnic/ \
  /mnt/user/photo-test-backups/Family-2024-Picnic/
```

Use this only for targeted first-run safety. For full ongoing backups, rely on the server's normal backup plan rather than making Immich Tagger responsible for backups.

## Immich sidecar Discover and Sync

For Immich external libraries, new sidecars beside already-known assets usually need Immich sidecar jobs.

Recommended sequence after a live Immich Tagger run:

1. Run Immich sidecar **Discover** so Immich associates newly created `.xmp` files.
2. After Discover finishes, run Immich sidecar **Sync** so Immich re-reads known/updated sidecars.

Immich rejects overlapping sidecar jobs, so Discover and Sync should not be started at the same time.

## Troubleshooting

### The app cannot reach Ollama

Check:

- Ollama server is running.
- URL includes `http://` and port `11434`.
- Docker container can reach that IP/host.
- Firewall allows access.
- Model exists on the Ollama server.

### Model is slow

Try:

- smaller folder;
- a test limit such as 10 images;
- `Max Image Size = 1440` instead of full-res;
- a smaller/fallback model if quality tradeoff is acceptable.

### Sidecars are not visible in Immich

Check:

- `.jpg.xmp` files exist next to photos;
- Immich external library points to the same files;
- Immich sidecar Discover has run;
- Immich sidecar Sync has run after Discover;
- no sidecar job is already running/stuck.

### Permission issues

On Unraid, check container values:

```text
PUID=99
PGID=100
UMASK=000
```

Do not recursively chmod appdata or media libraries from inside Immich Tagger. Use Unraid-safe maintenance practices if permissions need separate repair.

### A few files fail

A very small number of failures across a large library can be acceptable. The earlier full-library run had only 3-4 total files that could not be processed out of about 27,000, which is not worth delaying Docker/server work.
