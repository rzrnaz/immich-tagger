# Immich Tagger Docker/Unraid Migration Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Rename the product direction from PhotoAIApp to Immich Tagger and add a Docker/Unraid server version that can run background photo-tagging jobs without tying up the Windows PC.

**Architecture:** Keep the proven scanner and sidecar logic in a shared .NET core library. Keep the Windows WinForms GUI as a desktop client while adding a Linux/Docker-friendly server host with a small web UI/API and an optional CLI path for scripted or scheduled runs. The Docker image should operate on mounted Unraid paths, persist configuration/logs under `/config`, and call an Ollama server over HTTP rather than bundling model inference into the app container.

**Tech Stack:** .NET, ASP.NET Core minimal API/Razor or lightweight MVC for the server UI, existing PhotoAIApp.Core scanner, Docker Linux container, Unraid XML template, Ollama HTTP API, Immich XMP sidecar workflow.

---

## Product naming decisions

Use these names from this point forward:

- Product name: `Immich Tagger`
- Short slug: `immich-tagger`
- Docker image target: `ghcr.io/rzrnaz/immich-tagger-public`
- Future Windows executable: `ImmichTagger.exe`
- Unraid appdata path: `/mnt/user/appdata/immich-tagger`
- Container config path: `/config`
- Container photo library path: `/photos`
- Container logs path: `/config/logs`

Keep `PhotoAIApp` names temporarily where a full source rename would add risk. Prefer visible user-facing labels and new Docker/server artifacts to say `Immich Tagger` now.

## Scope boundaries

### In scope

- Documentation and rename direction.
- Server/Docker architecture.
- Unraid template with rich defaults.
- Web UI/API for manual runs, progress, cancellation, and logs.
- CLI/server shared configuration model.
- Preserve Windows GUI behavior while core is reused.
- Keep tags enabled by default because tagging is the primary product value.

### Out of scope for first Docker release

- Running the existing WinForms GUI inside Docker.
- Bundling Ollama or vision models inside the Immich Tagger image.
- Multi-user auth, public internet exposure, or reverse-proxy hardening.
- Complex scheduling/watch-folder automation.
- Automatic recursive chmod/permission repair of media libraries.

## Acceptance criteria

- Existing Windows GUI still builds on Windows.
- Existing scanner tests still pass where dotnet is available.
- Docker image builds on Linux.
- Container starts on Unraid with `/photos` and `/config` mappings.
- Server UI can run a dry run against a mounted subdirectory.
- Server UI can run a live scan against a mounted subdirectory.
- JSON and `.jpg.xmp` sidecars are written next to input photos.
- Logs persist under `/config/logs` or the run root `.photoai` path according to current scanner behavior.
- Stop/cancel does not leave the UI stuck.
- Ollama primary/fallback settings are configurable from environment/template and the UI.
- Unraid template exposes the main settings with helpful descriptions.
- User manual and developer manual describe the final behavior.

---

## Phase 0: Protect current work

### Task 0.1: Record current working tree state

**Objective:** Avoid losing existing accepted GUI/source changes before starting the Docker migration.

**Files:** none.

**Steps:**

1. Run:
   ```bash
   git status --short --branch
   git diff --stat
   ```
2. Confirm whether existing modified files are intentional carried-forward work.
3. Do not discard or overwrite existing modifications without explicit user approval.
4. If a clean checkpoint is needed, commit the existing changes separately before large refactors.

**Verification:** `git status --short` shows only intentional modifications.

### Task 0.2: Create a migration branch

**Objective:** Keep Docker/server work isolated from `main`.

**Steps:**

```bash
git checkout -b feat/immich-tagger-docker-docs
```

**Verification:**

```bash
git branch --show-current
# expected: feat/immich-tagger-docker-docs
```

---

## Phase 1: Documentation foundation

### Task 1.1: Add user manual draft

**Objective:** Document how Immich Tagger is intended to be used by a non-developer.

**Files:**

- Create: `docs/user-manual.md`

**Content requirements:**

- Purpose of the tool.
- What files are read/written.
- Explanation of tags vs descriptions.
- Explanation of JSON sidecars and XMP sidecars.
- Server addresses, models, ports, directories, switches, and settings.
- Dry-run workflow.
- Live-run workflow.
- Example processing/backing up a single Unraid subdirectory.
- Immich Discover then Sync guidance.
- Troubleshooting notes.

**Verification:** Read the manual end-to-end and confirm it can stand alone without prior chat context.

### Task 1.2: Add developer manual draft

**Objective:** Give future developers the architecture map and a cross-reference from user-facing settings to code/config/UI locations.

**Files:**

- Create: `docs/developer-manual.md`

**Content requirements:**

- Current solution structure.
- Target server/Docker structure.
- Core scanner responsibilities.
- WinForms responsibilities.
- Server host responsibilities.
- Configuration mapping table/list.
- Logs and diagnostics surfaces.
- UI theme/naming conventions.
- Release/build expectations.
- Docker/Unraid safety boundaries.

**Verification:** Manual lists where to find or add each user-facing feature.

### Task 1.3: Add Docker/Unraid README and template draft

**Objective:** Establish the installation shape before code is implemented.

**Files:**

- Create: `docker/README.md`
- Create: `docker/unraid/immich-tagger.xml`

**Verification:** Template contains `/photos`, `/config`, web port, Ollama settings, model defaults, `PUID`, `PGID`, `UMASK`, and timezone.

---

## Phase 2: Rename user-visible product surfaces

### Task 2.1: Update README to introduce Immich Tagger

**Objective:** Keep legacy repo context while making the new product name obvious.

**Files:**

- Modify: `README.md`

**Implementation notes:**

- Title should become `Immich Tagger`.
- First paragraph should mention formerly PhotoAIApp.
- Keep current Windows release notes until a new release is cut.
- Update feature list so tags are primary and always-on for current GUI direction.

**Verification:** README explains both current Windows GUI and future Docker direction.

### Task 2.2: Rename visible GUI labels later, not first

**Objective:** Avoid risky broad renames until Docker/server architecture is committed.

**Files:**

- Later modify: `PhotoAIApp.Gui/MainForm.cs`
- Later modify: `PhotoAIApp.Gui/PhotoAIApp.Gui.csproj`

**Implementation notes:**

- Update title bar to `Immich Tagger`.
- Update help dialog title.
- Update app icon title metadata if practical.
- Keep namespaces and project filenames until a dedicated rename task.

**Verification:** GUI still launches and existing tests pass.

---

## Phase 3: Extract server-ready configuration

### Task 3.1: Add shared configuration model

**Objective:** Represent all GUI/server/CLI settings in one reusable type.

**Files:**

- Create: `PhotoAIApp.Core/ImmichTaggerSettings.cs`
- Modify: `PhotoAIApp.Core/PhotoAiScanOptions.cs` if needed.
- Test: `PhotoAIApp.Tests/Program.cs`

**Settings to include:**

- `PhotoRootPath`, default `/photos` for Docker.
- `ConfigPath`, default `/config` for Docker.
- `LogPath`, default `/config/logs` for Docker/server.
- `Recursive`, default `true`.
- `Force`, default `true`.
- `WriteJson`, default `true`.
- `WriteXmp`, default `true`.
- `AddTags`, default `true`.
- `DryRun`, default `true` for safety.
- `OverwriteSidecars`, default configurable but conservative in docs.
- `PrimaryOllamaBaseUrl`, default `http://192.168.1.8:11434` for Unraid server template.
- `PrimaryModel`, default `qwen2.5vl:7b`.
- `MaxImageSize`, default `0` or `1440` according to selected preset.
- `FallbackEnabled`, default `true` if applicable.
- `FallbackOllamaBaseUrl`, default `http://192.168.1.8:11434` or blank depending deployment.
- `FallbackModel`, default `minicpm-v:latest`.
- `Port`, default `8080` inside container.

**Verification:** Tests confirm defaults match the manuals and template.

### Task 3.2: Add environment-variable binding

**Objective:** Let Docker/Unraid template values configure the server without editing files.

**Files:**

- Create or modify server project settings later.
- Test config binding where practical.

**Environment variable prefix:**

```text
IMMICH_TAGGER__
```

**Examples:**

```text
IMMICH_TAGGER__PHOTO_ROOT=/photos
IMMICH_TAGGER__PRIMARY_OLLAMA_URL=http://192.168.1.4:11434
IMMICH_TAGGER__PRIMARY_MODEL=qwen2.5vl:7b
IMMICH_TAGGER__MAX_IMAGE_SIZE=0
IMMICH_TAGGER__FALLBACK_ENABLED=true
IMMICH_TAGGER__FALLBACK_MODEL=minicpm-v:latest
```

**Verification:** Starting the server with env vars produces matching effective settings in the UI/settings page.

---

## Phase 4: Add server host

### Task 4.1: Create ASP.NET Core server project

**Objective:** Add a Docker-friendly web/API host that references the existing core library.

**Files:**

- Create: `PhotoAIApp.Server/PhotoAIApp.Server.csproj`
- Create: `PhotoAIApp.Server/Program.cs`
- Modify: `PhotoAIApp.sln`

**Implementation notes:**

- Use .NET minimal API or Razor Pages.
- Bind to `http://0.0.0.0:8080` in container.
- Reference `PhotoAIApp.Core`.
- Initial routes:
  - `GET /` status page.
  - `GET /settings` settings view or JSON endpoint.
  - `POST /api/dry-run` start dry run.
  - `POST /api/run` start live scan.
  - `POST /api/cancel` cancel current run.
  - `GET /api/status` current progress snapshot.
  - `GET /api/logs/latest` latest log lines.

**Verification:**

```bash
dotnet build PhotoAIApp.sln
```

### Task 4.2: Implement single-run background coordinator

**Objective:** Prevent multiple overlapping scans and support cancellation.

**Files:**

- Create: `PhotoAIApp.Server/ScanJobService.cs`

**Behavior:**

- Only one scan at a time.
- Reject new run if active.
- Store latest progress snapshot and recent log lines.
- Keep final summary for UI.
- Support cancellation token.

**Verification:** Tests or manual API calls show second run is rejected while first is active.

### Task 4.3: Add minimal web UI

**Objective:** Provide an Unraid-friendly browser UI.

**Files:**

- Create server UI files according to chosen ASP.NET pattern.

**Minimum UI fields:**

- Source folder under `/photos`.
- Dry Run button.
- Run Scan button.
- Stop button.
- Primary Ollama URL/model.
- Max Image Size.
- Fallback enabled/URL/model.
- Recursive, overwrite, write JSON, write XMP.
- Progress/status counters.
- Recent log lines.
- Link to full log when available.

**Verification:** User can perform a dry run from a browser without editing config files.

---

## Phase 5: Docker packaging

### Task 5.1: Add Dockerfile

**Objective:** Build and run the server app as a Linux container.

**Files:**

- Create: `Dockerfile`
- Create or update: `.dockerignore`

**Expected shape:**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish PhotoAIApp.Server/PhotoAIApp.Server.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PhotoAIApp.Server.dll"]
```

**Verification:**

```bash
docker build -t immich-tagger:local .
docker run --rm -p 8080:8080 -v /tmp/photos:/photos -v /tmp/immich-tagger-config:/config immich-tagger:local
```

### Task 5.2: Add compose example

**Objective:** Make local/server testing easy outside Unraid.

**Files:**

- Create: `docker/docker-compose.example.yml`

**Verification:** `docker compose up` starts the app.

### Task 5.3: Complete Unraid template

**Objective:** Make install-time settings clear in Unraid.

**Files:**

- Modify: `docker/unraid/immich-tagger.xml`

**Template must include:**

- Web UI port.
- `/photos` path.
- `/config` path.
- `PUID=99`.
- `PGID=100`.
- `UMASK=000`.
- `TZ`.
- Primary Ollama URL/model.
- Max Image Size.
- Fallback enabled/URL/model.
- Dry run default.
- Overwrite default.
- Write JSON/XMP defaults.

**Verification:** XML is syntactically valid and fields match `docs/user-manual.md`.

---

## Phase 6: Testing and release

### Task 6.1: Add server tests where practical

**Objective:** Protect config binding and job coordinator behavior.

**Files:**

- Modify: `PhotoAIApp.Tests/Program.cs` or add structured test project later.

**Verification:**

```powershell
dotnet run --project .\PhotoAIApp.Tests\PhotoAIApp.Tests.csproj
```

### Task 6.2: Manual Unraid smoke test

**Objective:** Prove the Docker version works against a small mounted folder.

**Steps:**

1. Create or choose one small subdirectory under the photo share.
2. Back up that directory or run on copies first.
3. Run dry run.
4. Confirm would-process counts.
5. Run live scan.
6. Confirm `.jpg.xmp` and `.photoai/*.json` output.
7. Confirm permissions match expected Unraid media policy.
8. Trigger Immich Discover then Sync if enabled/desired.

**Verification:** At least one real photo gets tags in XMP and no unexpected files are modified.

### Task 6.3: Release assets

**Objective:** Publish source-backed release when accepted.

**Files/artifacts:**

- GitHub tag/release.
- Windows ZIP/EXE if still desired.
- Docker image `ghcr.io/rzrnaz/immich-tagger-public:<version>`.
- Unraid XML template.
- Checksums.

**Verification:** Download/hash-verify release assets before reporting success.

---

## Immediate next checkpoint

The first checkpoint is documentation-only plus template scaffold:

- `docs/plans/2026-05-27-immich-tagger-docker-migration.md`
- `docs/user-manual.md`
- `docs/developer-manual.md`
- `docker/README.md`
- `docker/unraid/immich-tagger.xml`
- README renamed/updated to introduce Immich Tagger

After that, proceed to code refactor/server project creation.
