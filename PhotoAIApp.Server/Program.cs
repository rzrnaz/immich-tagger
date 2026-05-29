using ImmichTagger.Server;
using PhotoAIApp.Core;
using System.Text.Encodings.Web;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "IMMICH_TAGGER__");
ImmichTaggerSettings initialSettings = builder.Configuration.Get<ImmichTaggerSettings>() ?? new ImmichTaggerSettings();
LoadPersistedSettings(initialSettings);
NormalizeConfiguredSettings(initialSettings);
builder.Services.AddSingleton(initialSettings);
builder.Services.AddSingleton<ScanJobService>();

var app = builder.Build();

app.MapGet("/", (ImmichTaggerSettings settings, ScanJobService jobs) => Results.Content(RenderHome(settings, jobs.Status), "text/html"));
app.MapGet("/favicon.ico", () =>
{
    string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PhotoAIApp.ico");
    return System.IO.File.Exists(iconPath)
        ? Results.File(iconPath, "image/x-icon")
        : Results.NotFound();
});
app.MapGet("/icon.png", () =>
{
    string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PhotoAIApp.png");
    return System.IO.File.Exists(iconPath)
        ? Results.File(iconPath, "image/png")
        : Results.NotFound();
});
app.MapGet("/healthz", (ScanJobService jobs) => Results.Ok(new
{
    status = "ok",
    isRunning = jobs.Status.IsRunning,
    checkedAt = DateTimeOffset.UtcNow
}));
app.MapGet("/api/settings", (ImmichTaggerSettings settings) => Results.Ok(settings));
app.MapPost("/api/settings", (ImmichTaggerSettings settings, ImmichTaggerSettingsUpdate request) =>
{
    lock (settings)
    {
        ApplySettingsUpdate(settings, request);
        SavePersistedSettings(settings);
    }

    return Results.Ok(settings);
});
app.MapGet("/api/status", (ScanJobService jobs) => Results.Ok(jobs.Status));

app.MapGet("/api/folders", (ImmichTaggerSettings settings, string? path) =>
{
    string requestedPath = string.IsNullOrWhiteSpace(path) ? settings.PhotoRoot : path;
    if (!TryValidateFolderPath(settings, requestedPath, out string fullPath, out string? error))
    {
        return Results.BadRequest(new { message = error });
    }

    string[] directories = Directory.EnumerateDirectories(fullPath)
        .Where(directory => !PhotoAiScanner.IsExcludedScanDirectoryPath(directory, settings.PhotoRoot))
        .OrderBy(directory => Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)
        .ToArray();

    string? parent = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar) == Path.GetFullPath(settings.PhotoRoot).TrimEnd(Path.DirectorySeparatorChar)
        ? null
        : Directory.GetParent(fullPath)?.FullName;

    return Results.Ok(new FolderBrowseResponse(fullPath, parent, directories));
});

app.MapGet("/api/log", (ImmichTaggerSettings settings, string path) =>
{
    if (!ValidatePathForRead(path, [settings.PhotoRoot, settings.LogRoot, settings.ConfigRoot], out string fullPath, out string? error))
    {
        return Results.BadRequest(new { message = error });
    }

    if (!System.IO.File.Exists(fullPath))
    {
        return Results.NotFound(new { message = $"Log file does not exist: {fullPath}" });
    }

    return Results.File(fullPath, "text/plain");
});

app.MapGet("/api/models", async (string baseUrl, string? target) =>
{
    string normalizedBaseUrl = NormalizeOllamaBaseUrl(baseUrl);
    bool isFallback = string.Equals(target, "fallback", StringComparison.OrdinalIgnoreCase);

    try
    {
        var scanner = new PhotoAiScanner();
        string[] models = NormalizeModelListForTarget(await scanner.GetAvailableOllamaModelsAsync(normalizedBaseUrl), isFallback);
        return Results.Ok(new OllamaModelsResponse(normalizedBaseUrl, models));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = $"Unable to query {normalizedBaseUrl}: {ex.Message}" });
    }
});

app.MapPost("/api/dry-run", (ImmichTaggerSettings settings, ScanJobService jobs, ScanStartRequest request) =>
{
    if (!TryNormalizeScanRequest(settings, request, out ScanStartRequest normalizedRequest, out string? error))
    {
        return Results.BadRequest(new { message = error });
    }

    return jobs.TryStart(settings, normalizedRequest, dryRun: true, out string message)
        ? Results.Accepted("/api/status", new { message })
        : Results.Conflict(new { message });
});

app.MapPost("/api/run", (ImmichTaggerSettings settings, ScanJobService jobs, ScanStartRequest request) =>
{
    if (!TryNormalizeScanRequest(settings, request, out ScanStartRequest normalizedRequest, out string? error))
    {
        return Results.BadRequest(new { message = error });
    }

    if (!TryValidateLiveRunWritableOutputs(normalizedRequest, out error))
    {
        return Results.BadRequest(new { message = error });
    }

    return jobs.TryStart(settings, normalizedRequest, dryRun: false, out string message)
        ? Results.Accepted("/api/status", new { message })
        : Results.Conflict(new { message });
});

app.MapPost("/api/cancel", (ScanJobService jobs) =>
{
    return jobs.Cancel(out string message)
        ? Results.Accepted("/api/status", new { message })
        : Results.NotFound(new { message });
});

app.MapPost("/api/pause", (ScanJobService jobs) =>
{
    return jobs.Pause(out string message)
        ? Results.Accepted("/api/status", new { message })
        : Results.Conflict(new { message });
});

app.MapPost("/api/resume", (ScanJobService jobs) =>
{
    return jobs.Resume(out string message)
        ? Results.Accepted("/api/status", new { message })
        : Results.Conflict(new { message });
});

app.Run();

static void ApplySettingsUpdate(ImmichTaggerSettings settings, ImmichTaggerSettingsUpdate request)
{
    settings.PhotoRoot = CleanPath(request.PhotoRoot, settings.PhotoRoot);
    settings.ConfigRoot = CleanPath(request.ConfigRoot, settings.ConfigRoot);
    settings.LogRoot = CleanPath(request.LogRoot, settings.LogRoot);
    settings.DefaultFolderPath = settings.PhotoRoot;
    settings.Recursive = request.Recursive;
    settings.Force = request.Force;
    settings.WriteJson = request.WriteJson;
    settings.WriteXmp = request.WriteXmp;
    settings.AddTags = request.AddTags;
    settings.OverwriteSidecars = request.OverwriteSidecars;
    settings.Limit = request.Limit is > 0 ? request.Limit : null;
    settings.PrimaryOllamaUrl = NormalizeConfiguredOllamaBaseUrl(request.PrimaryOllamaUrl, settings.PrimaryOllamaUrl);
    settings.PrimaryModel = CleanText(request.PrimaryModel, settings.PrimaryModel);
    settings.MaxImageSize = Math.Max(0, request.MaxImageSize);
    settings.FallbackEnabled = request.FallbackEnabled;
    settings.FallbackOllamaUrl = NormalizeConfiguredOllamaBaseUrl(request.FallbackOllamaUrl, settings.FallbackOllamaUrl);
    settings.FallbackModel = CleanText(request.FallbackModel, settings.FallbackModel);
    settings.FallbackMaxImageSize = Math.Max(0, request.FallbackMaxImageSize);
    settings.SyncImmich = request.SyncImmich;
    settings.ImmichBaseUrl = CleanText(request.ImmichBaseUrl, settings.ImmichBaseUrl);
    settings.ImmichApiKey = CleanText(request.ImmichApiKey, settings.ImmichApiKey);
}

static string CleanText(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

static string NormalizeConfiguredOllamaBaseUrl(string? value, string fallback)
{
    string candidate = CleanText(value, fallback);
    return NormalizeOllamaBaseUrl(candidate);
}

static string CleanPath(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : Path.GetFullPath(value.Trim());

static bool ValidatePathForRead(string requestedPath, IReadOnlyList<string> allowedRoots, out string fullPath, out string? error)
{
    fullPath = string.Empty;
    error = null;

    if (string.IsNullOrWhiteSpace(requestedPath))
    {
        error = "Path is required.";
        return false;
    }

    fullPath = Path.GetFullPath(requestedPath);
    foreach (string allowedRoot in allowedRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
    {
        string fullRoot = Path.GetFullPath(allowedRoot);
        string rootWithSeparator = fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (string.Equals(fullPath, fullRoot, StringComparison.Ordinal)
            || fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            return true;
        }
    }

    error = $"Path must stay under one of the configured safe roots: {string.Join(", ", allowedRoots)}";
    return false;
}

static bool TryValidateFolderPath(ImmichTaggerSettings settings, string requestedPath, out string fullPath, out string? error)
{
    fullPath = string.Empty;

    if (!ValidatePathForRead(requestedPath, [settings.PhotoRoot], out string candidatePath, out error))
    {
        return false;
    }

    if (!Directory.Exists(candidatePath))
    {
        error = $"Folder does not exist: {candidatePath}";
        return false;
    }

    if (PhotoAiScanner.IsExcludedScanDirectoryPath(candidatePath, settings.PhotoRoot))
    {
        error = $"Folder is an internal/system folder and cannot be scanned: {candidatePath}";
        return false;
    }

    fullPath = candidatePath;
    return true;
}

static bool TryNormalizeScanRequest(ImmichTaggerSettings settings, ScanStartRequest request, out ScanStartRequest normalizedRequest, out string? error)
{
    try
    {
        string[] normalizedFolders = GetRequestedFolders(settings, request);
        normalizedRequest = request with
        {
            FolderPath = normalizedFolders[0],
            FolderPaths = normalizedFolders
        };
        error = null;
        return true;
    }
    catch (Exception ex)
    {
        normalizedRequest = request;
        error = ex.Message;
        return false;
    }
}

static bool TryValidateLiveRunWritableOutputs(ScanStartRequest request, out string? error)
{
    PhotoAiLiveRunPreflightResult preflight = PhotoAiLiveRunPreflight.ValidateWritableOutputs(request.FolderPaths ?? []);
    if (preflight.CanRun)
    {
        error = null;
        return true;
    }

    error = $"Live scan blocked: {string.Join(" ", preflight.Errors)}";
    return false;
}

static string[] GetRequestedFolders(ImmichTaggerSettings settings, ScanStartRequest request)
{
    IEnumerable<string?> requestedFolders = request.FolderPaths is { Count: > 0 }
        ? request.FolderPaths
        : [string.IsNullOrWhiteSpace(request.FolderPath) ? settings.PhotoRoot : request.FolderPath];

    return PhotoAiFolderSelection.NormalizeAndValidateSelectedFolders(requestedFolders, settings.PhotoRoot);
}

static string NormalizeOllamaBaseUrl(string? baseUrl)
{
    return PhotoAiModelProfile.BuildBaseUrl(baseUrl ?? string.Empty, 11434);
}

static void NormalizeConfiguredSettings(ImmichTaggerSettings settings)
{
    settings.PrimaryOllamaUrl = NormalizeOllamaBaseUrl(settings.PrimaryOllamaUrl);
    settings.FallbackOllamaUrl = NormalizeOllamaBaseUrl(settings.FallbackOllamaUrl);
}

static string[] NormalizeModelListForTarget(IEnumerable<string> discoveredModels, bool isFallback)
{
    return discoveredModels
        .Where(model => !string.IsNullOrWhiteSpace(model))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string GetSettingsFilePath(ImmichTaggerSettings settings)
{
    string configRoot = string.IsNullOrWhiteSpace(settings.ConfigRoot) ? "/config" : settings.ConfigRoot;
    return Path.Combine(Path.GetFullPath(configRoot), "immich-tagger-settings.json");
}

static void LoadPersistedSettings(ImmichTaggerSettings settings)
{
    string settingsFilePath = GetSettingsFilePath(settings);
    if (!System.IO.File.Exists(settingsFilePath))
    {
        return;
    }

    string json = System.IO.File.ReadAllText(settingsFilePath);
    ImmichTaggerSettings? savedSettings = JsonSerializer.Deserialize<ImmichTaggerSettings>(json, CreateJsonOptions());
    if (savedSettings is null)
    {
        return;
    }

    CopySettings(savedSettings, settings);
}

static void SavePersistedSettings(ImmichTaggerSettings settings)
{
    string settingsFilePath = GetSettingsFilePath(settings);
    Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
    string json = JsonSerializer.Serialize(settings, CreateJsonOptions());
    System.IO.File.WriteAllText(settingsFilePath, json);
}

static void CopySettings(ImmichTaggerSettings source, ImmichTaggerSettings target)
{
    target.PhotoRoot = source.PhotoRoot;
    target.ConfigRoot = source.ConfigRoot;
    target.LogRoot = source.LogRoot;
    target.DefaultFolderPath = source.DefaultFolderPath;
    target.Recursive = source.Recursive;
    target.Force = source.Force;
    target.WriteJson = source.WriteJson;
    target.WriteXmp = source.WriteXmp;
    target.AddTags = source.AddTags;
    target.DryRunDefault = source.DryRunDefault;
    target.OverwriteSidecars = source.OverwriteSidecars;
    target.Limit = source.Limit;
    target.PrimaryOllamaUrl = NormalizeOllamaBaseUrl(source.PrimaryOllamaUrl);
    target.PrimaryModel = source.PrimaryModel;
    target.MaxImageSize = source.MaxImageSize;
    target.FallbackEnabled = source.FallbackEnabled;
    target.FallbackOllamaUrl = NormalizeOllamaBaseUrl(source.FallbackOllamaUrl);
    target.FallbackModel = source.FallbackModel;
    target.FallbackMaxImageSize = source.FallbackMaxImageSize;
    target.SyncImmich = source.SyncImmich;
    target.ImmichBaseUrl = source.ImmichBaseUrl;
    target.ImmichApiKey = source.ImmichApiKey;
}

static JsonSerializerOptions CreateJsonOptions() => new()
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
};

static string RenderHome(ImmichTaggerSettings settings, ScanJobStatus status)
{
    string Encode(string? value) => HtmlEncoder.Default.Encode(value ?? string.Empty);
    string Checked(bool value) => value ? "checked" : string.Empty;
    string[] explicitSelectedFolders = status.IsRunning && status.SelectedFolderPaths.Count > 0
        ? status.SelectedFolderPaths.ToArray()
        : [];
    string currentFolder = status.IsRunning
        ? explicitSelectedFolders.FirstOrDefault() ?? (string.IsNullOrWhiteSpace(status.FolderPath) ? settings.PhotoRoot : status.FolderPath)
        : settings.PhotoRoot;
    string folder = Encode(currentFolder);
    string message = Encode(status.Message);
    string error = Encode(status.Error ?? string.Empty);
    string recentLog = string.Join("\n", status.RecentLogLines.Select(HtmlEncoder.Default.Encode));
    string running = status.IsRunning ? (status.IsPaused ? "Paused" : "Running") : "Idle";
    string started = status.StartedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "n/a";
    string finished = status.FinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "n/a";
    string progress = RenderProgress(status.LastProgress);
    string summary = status.Summary is null ? string.Empty : $"<pre>{Encode(status.Summary.PlainText)}</pre>";
    string? runLogPath = status.RunLogPath ?? ExtractFirstLogPath(status.Summary?.PlainText);
    string openLogLink = string.IsNullOrWhiteSpace(runLogPath)
        ? "<span>No run log available yet.</span>"
        : $"<a id=\"openLogLink\" href=\"/api/log?path={Uri.EscapeDataString(runLogPath)}\" target=\"_blank\">Open Log</a>";
    string selectedFoldersJson = JsonSerializer.Serialize(explicitSelectedFolders);
    string scanButtonsDisabled = status.IsRunning ? "disabled" : string.Empty;
    string activeScanButtonsDisabled = status.IsRunning ? string.Empty : "disabled";
    string selectedFolderSummary = explicitSelectedFolders.Length switch
    {
        > 1 => $"{explicitSelectedFolders.Length} selected folders",
        1 => Encode(explicitSelectedFolders[0]),
        _ => Encode(currentFolder)
    };

    PhotoAiModelProfile currentProfile = BuildProfileFromSettings(settings);
    PhotoAiModelProfileId currentProfileId = PhotoAiModelProfile.MatchPreset(currentProfile);
    string currentProfileSummary = currentProfileId == PhotoAiModelProfileId.Custom
        ? $"Custom | {currentProfile.Summary}"
        : currentProfile.Summary;
    string currentProfileIdText = currentProfileId.ToString();
    string fallbackPresetId = string.Equals(currentProfile.FallbackOllamaBaseUrl, PhotoAiDefaults.UnraidOllamaBaseUrl, StringComparison.OrdinalIgnoreCase)
        && string.Equals(currentProfile.FallbackModel, PhotoAiDefaults.QwenPcModel, StringComparison.OrdinalIgnoreCase)
        && currentProfile.FallbackMaxImageDimensionPixels == PhotoAiDefaults.QwenMaxImageDimensionPixels
            ? "Balanced"
            : string.Equals(currentProfile.FallbackOllamaBaseUrl, PhotoAiDefaults.UnraidOllamaBaseUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentProfile.FallbackModel, PhotoAiDefaults.UnraidModel, StringComparison.OrdinalIgnoreCase)
                && currentProfile.FallbackMaxImageDimensionPixels == PhotoAiDefaults.UnraidMaxImageDimensionPixels
                    ? "Compatibility"
                    : "Custom";
    string profilePresetsJson = JsonSerializer.Serialize(PhotoAiModelProfile.Presets.Select(profile => new
    {
        id = profile.ProfileId.ToString(),
        displayName = profile.DisplayName,
        summary = profile.Summary,
        primaryOllamaUrl = profile.OllamaBaseUrl,
        primaryModel = profile.Model,
        maxImageSize = profile.MaxImageDimensionPixels,
        fallbackEnabled = profile.ModelPreference == PhotoAiModelPreference.QwenPcWithUnraidFallback,
        fallbackOllamaUrl = profile.FallbackOllamaBaseUrl,
        fallbackModel = profile.FallbackModel,
        fallbackMaxImageSize = profile.FallbackMaxImageDimensionPixels
    }));
    string fallbackPresetsJson = JsonSerializer.Serialize(new[]
    {
        new
        {
            id = "Balanced",
            displayName = "Balanced - Unraid Qwen 7B 1440px",
            summary = "Fallback target: 192.168.1.8:11434 | Model: qwen2.5vl:7b | Max Image Size: 1440px",
            fallbackOllamaUrl = PhotoAiDefaults.UnraidOllamaBaseUrl,
            fallbackModel = PhotoAiDefaults.QwenPcModel,
            fallbackMaxImageSize = PhotoAiDefaults.QwenMaxImageDimensionPixels
        },
        new
        {
            id = "Compatibility",
            displayName = "Compatibility - Unraid MiniCPM-V full-res",
            summary = "Fallback target: 192.168.1.8:11434 | Model: minicpm-v:latest | Max Image Size: Original/full-res",
            fallbackOllamaUrl = PhotoAiDefaults.UnraidOllamaBaseUrl,
            fallbackModel = PhotoAiDefaults.UnraidModel,
            fallbackMaxImageSize = PhotoAiDefaults.UnraidMaxImageDimensionPixels
        }
    });

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Immich Tagger</title>
  <link rel="icon" href="/favicon.ico" sizes="any">
  <link rel="icon" type="image/png" href="/icon.png" sizes="256x256">
  <style>
    body { font-family: system-ui, Segoe UI, sans-serif; background: #f7f1e8; color: #111; margin: 0; }
    main { max-width: 1240px; margin: 0 auto; padding: 32px; }
    .card { background: #fffaf2; border: 1px solid #d8c3a5; border-radius: 16px; padding: 20px; margin: 16px 0; box-shadow: 0 2px 10px #00000012; }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(270px, 1fr)); gap: 16px; }
    .scan-actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; }
    label { display: block; font-weight: 650; margin: 12px 0 6px; }
    input, select { box-sizing: border-box; width: 100%; padding: 10px; border-radius: 8px; border: 1px solid #c9b79c; font-size: 1rem; background: #fff; }
    input[type="checkbox"] { width: auto; margin-right: 8px; }
    button, a.button { border: 0; border-radius: 10px; padding: 11px 18px; margin: 8px 8px 0 0; color: #f7f1e8; background: #2f75b5; font-weight: 700; cursor: pointer; text-decoration: none; display: inline-block; }
    button:disabled { opacity: 0.55; cursor: not-allowed; }
    button.stop { background: #b64234; }
    button.pause { background: #d4a72c; color: #111; }
    button.secondary { background: #9b6f3d; }
    button.ghost { background: #efe2cf; color: #111; }
    dl { display: grid; grid-template-columns: 180px 1fr; gap: 8px; }
    dt { font-weight: 700; }
    pre { white-space: pre-wrap; background: #211f1c; color: #f7f1e8; border-radius: 12px; padding: 16px; min-height: 100px; overflow: auto; }
    .error { color: #a1261d; font-weight: 700; }
    .folder-list { border: 1px solid #d8c3a5; border-radius: 12px; background: #f3e8d7; padding: 12px; margin-top: 12px; }
    .folder-list button { width: auto; text-align: left; background: #efe2cf; color: #111; margin: 6px 8px 0 0; }
    .folder-row { display: grid; grid-template-columns: auto 1fr auto auto; gap: 8px; align-items: center; margin: 8px 0; }
    .folder-row button { margin: 0; }
    .folder-browser-toolbar { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-bottom: 10px; }
    .selected-folders { margin: 12px 0; padding: 12px; border: 1px solid #d8c3a5; border-radius: 12px; background: #f3e8d7; }
    .selected-folder-header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; }
    .selected-folder-chip { display: flex; align-items: center; justify-content: space-between; gap: 8px; background: #fffaf2; border: 1px solid #d8c3a5; border-radius: 10px; padding: 8px 10px; margin: 8px 0; }
    .selected-folder-chip button { margin: 0; padding: 6px 10px; background: #9b6f3d; }
    .muted { color: #685f52; }
    .switches label { font-weight: 500; }
    .help-note { margin-top: 8px; }
    .status-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; margin: 16px 0; }
    .status-metric { background: #f3e8d7; border: 1px solid #d8c3a5; border-radius: 12px; padding: 12px; }
    .status-metric .label { font-size: 0.9rem; color: #685f52; margin-bottom: 4px; }
    .status-metric .value { font-size: 1.05rem; font-weight: 700; word-break: break-word; }
    .status-progress { margin: 16px 0; padding: 14px; border-radius: 12px; background: #f3e8d7; border: 1px solid #d8c3a5; }
    .status-progress progress { width: 100%; height: 22px; margin: 10px 0 14px; }
    .profile-summary { margin-top: 8px; padding: 10px 12px; border-radius: 10px; background: #f3e8d7; border: 1px solid #d8c3a5; }
    .settings-note { margin-top: 4px; }
    @media (max-width: 720px) {
      main { padding: 16px; }
      dl { grid-template-columns: 1fr; }
      .folder-row { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <main>
    <h1>Immich Tagger</h1>
    <p>AI-powered tags, descriptions, and XMP sidecars for Immich photo libraries.</p>

    <section class="card">
      <h2>Start a scan</h2>
      <label>Photo root</label>
      <div id="photoRootDisplay" class="profile-summary">{{Encode(settings.PhotoRoot)}}</div>
      <div class="scan-actions">
        <button class="secondary" type="button" onclick="browseFolders(currentBrowsePath || getEffectivePhotoRoot())">Browse current folder</button>
        <button class="secondary" type="button" onclick="browseFolders(getEffectivePhotoRoot())">Browse photo root</button>
      </div>
      <p class="muted help-note">Browse under the configured photo root and add one or more folders for this run. If nothing is selected, the default scan root stays the photo root shown above.</p>
      <div id="folderBrowser" class="folder-list muted">Folder browser will appear here.</div>
      <div class="selected-folders">
        <div class="selected-folder-header">
          <strong>Selected folders for this run</strong>
          <span id="selectedFolderCount" class="muted"></span>
        </div>
        <div id="selectedFoldersList" class="muted"></div>
        <div class="scan-actions">
          <button class="secondary" type="button" onclick="clearSelectedFolders()">Clear selected folders</button>
        </div>
      </div>
      <label for="limit">Limit, optional</label>
      <input id="limit" type="number" min="0" value="{{settings.Limit?.ToString() ?? string.Empty}}" placeholder="0 = no limit">
      <div class="scan-actions">
        <button class="pause" {{scanButtonsDisabled}} onclick="startScan('/api/dry-run')">Dry Run</button>
        <button {{scanButtonsDisabled}} onclick="startScan('/api/run')">Run Scan</button>
        <button class="pause" {{activeScanButtonsDisabled}} onclick="togglePauseResume()">{{(status.IsPaused ? "Resume" : "Pause")}}</button>
        <button class="stop" {{activeScanButtonsDisabled}} onclick="cancelScan()">Stop</button>
      </div>
    </section>

    <section class="card">
      <h2>Status: {{running}}</h2>
      <div class="status-grid">
        <div class="status-metric"><div class="label">Message</div><div class="value">{{message}}</div></div>
        <div class="status-metric"><div class="label">Selected folders</div><div id="statusSelectedFolderSummary" class="value">{{selectedFolderSummary}}</div></div>
        <div class="status-metric"><div class="label">Started</div><div class="value">{{started}}</div></div>
        <div class="status-metric"><div class="label">Finished</div><div class="value">{{finished}}</div></div>
        <div class="status-metric"><div class="label">Pause state</div><div class="value">{{(status.IsPaused ? "Paused" : (status.IsRunning ? "Running" : "Idle"))}}</div></div>
        <div class="status-metric"><div class="label">Open Log</div><div class="value">{{openLogLink}}</div></div>
      </div>
      {{progress}}
      <dl>
        <dt>Primary Ollama</dt><dd>{{Encode(settings.PrimaryOllamaUrl)}}</dd>
        <dt>Primary Model</dt><dd>{{Encode(settings.PrimaryModel)}}</dd>
        <dt>Max Image Size</dt><dd>{{(settings.MaxImageSize <= 0 ? "Original/full-res" : settings.MaxImageSize.ToString())}}</dd>
        <dt>Fallback</dt><dd>{{(settings.FallbackEnabled ? $"Enabled / {Encode(settings.FallbackModel)}" : "Disabled")}}</dd>
        <dt>Sync Immich</dt><dd>{{settings.SyncImmich}} / {{Encode(settings.ImmichBaseUrl)}}</dd>
      </dl>
      {{(string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class=\"error\">{error}</p>")}}
      {{summary}}
    </section>

    <section class="card">
      <h2>Settings</h2>
      <p class="muted">These values start from the Unraid template/environment defaults. Saving here writes /config/immich-tagger-settings.json so GUI edits survive container restarts. If startup values differ from the current template, the saved /config copy is winning. Update the Unraid template/container variables when you want to change the first-run defaults.</p>
      <div class="grid">
        <div>
          <label for="photoRoot">Photo root</label>
          <input id="photoRoot" value="{{Encode(settings.PhotoRoot)}}">
          <label for="configRoot">Config root</label>
          <input id="configRoot" value="{{Encode(settings.ConfigRoot)}}">
          <label for="logRoot">Log root</label>
          <input id="logRoot" value="{{Encode(settings.LogRoot)}}">
        </div>
        <div>
          <label for="profilePreset">Model preset</label>
          <select id="profilePreset" onchange="applyProfilePreset(this.value)">
            <option value="HighQuality"{{(currentProfileIdText == nameof(PhotoAiModelProfileId.HighQuality) ? " selected" : string.Empty)}}>High Quality - Qwen 7B full-res</option>
            <option value="Balanced"{{(currentProfileIdText == nameof(PhotoAiModelProfileId.Balanced) ? " selected" : string.Empty)}}>Balanced - Qwen 7B 1440px</option>
            <option value="Compatibility"{{(currentProfileIdText == nameof(PhotoAiModelProfileId.Compatibility) ? " selected" : string.Empty)}}>Compatibility - Unraid MiniCPM-V full-res</option>
            <option value="Custom"{{(currentProfileIdText == nameof(PhotoAiModelProfileId.Custom) ? " selected" : string.Empty)}}>Custom</option>
          </select>
          <div id="profileSummary" class="profile-summary">{{Encode(currentProfileSummary)}}</div>
          <p class="muted settings-note">Choose a preset like Windows 1.39, then fine-tune fields below if needed.</p>
          <label for="primaryOllamaUrl">Primary Ollama URL</label>
          <input id="primaryOllamaUrl" value="{{Encode(settings.PrimaryOllamaUrl)}}">
          <label for="primaryModel">Primary model</label>
          <select id="primaryModel"><option selected>{{Encode(settings.PrimaryModel)}}</option></select>
          <p id="primaryModelStatus" class="muted settings-note">Change the primary Ollama URL, then pick from the refreshed model dropdown.</p>
          <label for="maxImageSize">Max Image Size</label>
          <input id="maxImageSize" type="number" min="0" value="{{settings.MaxImageSize}}">
          <label for="fallbackPreset">Fallback preset</label>
          <select id="fallbackPreset" onchange="applyFallbackPreset(this.value)">
            <option value="Balanced"{{(fallbackPresetId == "Balanced" ? " selected" : string.Empty)}}>Balanced - Unraid Qwen 7B 1440px</option>
            <option value="Compatibility"{{(fallbackPresetId == "Compatibility" ? " selected" : string.Empty)}}>Compatibility - Unraid MiniCPM-V full-res</option>
            <option value="Custom"{{(fallbackPresetId == "Custom" ? " selected" : string.Empty)}}>Custom</option>
          </select>
          <p id="fallbackPresetSummary" class="muted settings-note">Balanced and Compatibility are the fallback-server shortcuts for the Unraid GPU path.</p>
          <label for="fallbackOllamaUrl">Fallback Ollama URL</label>
          <input id="fallbackOllamaUrl" value="{{Encode(settings.FallbackOllamaUrl)}}">
          <label for="fallbackModel">Fallback model</label>
          <select id="fallbackModel"><option selected>{{Encode(settings.FallbackModel)}}</option></select>
          <p id="fallbackModelStatus" class="muted settings-note">When fallback is enabled, this dropdown reloads from the fallback Ollama URL.</p>
          <label for="fallbackMaxImageSize">Fallback Max Image Size</label>
          <input id="fallbackMaxImageSize" type="number" min="0" value="{{settings.FallbackMaxImageSize}}">
          <label for="immichBaseUrl">Immich Base URL</label>
          <input id="immichBaseUrl" value="{{Encode(settings.ImmichBaseUrl)}}">
          <label for="immichApiKey">Immich API Key</label>
          <input id="immichApiKey" type="password" value="{{Encode(settings.ImmichApiKey)}}" placeholder="Leave blank to keep current value or disable sync">
        </div>
        <div class="switches">
          <label><input id="recursive" type="checkbox" {{Checked(settings.Recursive)}}>Subfolders</label>
          <label><input id="force" type="checkbox" {{Checked(settings.Force)}}>Scan existing PhotoAI sidecars</label>
          <label><input id="writeJson" type="checkbox" {{Checked(settings.WriteJson)}}>Model Log (.photoai.json diagnostics)</label>
          <label><input id="writeXmp" type="checkbox" {{Checked(settings.WriteXmp)}}>Write Immich XMP sidecars</label>
          <label><input id="addTags" type="checkbox" {{Checked(settings.AddTags)}}>Add tags</label>
          <label><input id="overwriteSidecars" type="checkbox" {{Checked(settings.OverwriteSidecars)}}>Overwrite sidecars</label>
          <label><input id="fallbackEnabled" type="checkbox" {{Checked(settings.FallbackEnabled)}}>Fallback enabled</label>
          <label><input id="syncImmich" type="checkbox" {{Checked(settings.SyncImmich)}}>Sync Immich after live scans</label>
          <button onclick="saveSettings()">Save Settings</button>
        </div>
      </div>
    </section>

    <section class="card">
      <h2>Recent log</h2>
      <pre>{{recentLog}}</pre>
    </section>
  </main>

  <script>
    const selectedFoldersStorageKey = 'immichTagger.selectedFolders';
    const selectedFoldersRestoreOnceKey = 'immichTagger.selectedFolders.restoreOnce';
    const photoRoot = '{{Encode(settings.PhotoRoot)}}';
    let selectedFolders = loadSelectedFolders();
    let currentFolderBrowserData = null;
    let currentBrowsePath = photoRoot;
    const profilePresets = {{profilePresetsJson}};
    const fallbackPresets = {{fallbackPresetsJson}};
    const initialProfilePresetId = '{{currentProfileIdText}}';

    function loadSelectedFolders() {
      const serverSelectedFolders = dedupeFolders({{selectedFoldersJson}});
      if (serverSelectedFolders.length > 0) {
        persistSelectedFolders(serverSelectedFolders, false);
        return serverSelectedFolders;
      }

      try {
        const shouldRestore = window.sessionStorage.getItem(selectedFoldersRestoreOnceKey) === 'true';
        window.sessionStorage.removeItem(selectedFoldersRestoreOnceKey);
        if (!shouldRestore) {
          return [];
        }

        const persisted = JSON.parse(window.sessionStorage.getItem(selectedFoldersStorageKey) || '[]');
        return Array.isArray(persisted) ? dedupeFolders(persisted) : [];
      } catch {
        window.sessionStorage.removeItem(selectedFoldersRestoreOnceKey);
        return [];
      }
    }

    function persistSelectedFolders(paths, restoreOnce = false) {
      const normalized = dedupeFolders(paths);
      window.sessionStorage.setItem(selectedFoldersStorageKey, JSON.stringify(normalized));
      if (restoreOnce && normalized.length > 0) {
        window.sessionStorage.setItem(selectedFoldersRestoreOnceKey, 'true');
      } else {
        window.sessionStorage.removeItem(selectedFoldersRestoreOnceKey);
      }
    }

    const numericValue = (id) => {
      const raw = document.getElementById(id).value;
      return raw === '' ? null : Number(raw);
    };

    function getEffectivePhotoRoot() {
      const configured = (document.getElementById('photoRoot')?.value || photoRoot).trim();
      return configured || photoRoot;
    }

    function updatePhotoRootDisplay() {
      const display = document.getElementById('photoRootDisplay');
      if (display) {
        display.textContent = getEffectivePhotoRoot();
      }
    }

    const checkboxValue = (id) => document.getElementById(id).checked;
    const textValue = (id) => document.getElementById(id).value;

    function dedupeFolders(paths) {
      const seen = new Set();
      const normalized = [];
      for (const path of paths) {
        const trimmed = (path || '').trim();
        if (!trimmed) continue;
        if (seen.has(trimmed)) continue;
        seen.add(trimmed);
        normalized.push(trimmed);
      }
      return normalized;
    }

    function renderSelectedFolders() {
      selectedFolders = dedupeFolders(selectedFolders);
      persistSelectedFolders(selectedFolders, false);
      updatePhotoRootDisplay();
      const host = document.getElementById('selectedFoldersList');
      const count = document.getElementById('selectedFolderCount');
      const summary = document.getElementById('statusSelectedFolderSummary');
      count.textContent = selectedFolders.length === 0
        ? 'No explicit folder selections yet'
        : `${selectedFolders.length} selected folder${selectedFolders.length === 1 ? '' : 's'}`;
      summary.textContent = selectedFolders.length > 1
        ? `${selectedFolders.length} selected folders`
        : (selectedFolders[0] || getEffectivePhotoRoot());
      if (selectedFolders.length === 0) {
        host.innerHTML = '<p>No folders selected yet. Use the folder browser to add one or more folders.</p>';
        if (currentFolderBrowserData) {
          renderFolderBrowser(currentFolderBrowserData);
        }
        return;
      }

      host.innerHTML = selectedFolders.map((path, index) => `
        <div class="selected-folder-chip">
          <span>${escapeHtml(path)}</span>
          <span>
            <button type="button" onclick="removeSelectedFolder(${index})">Remove</button>
          </span>
        </div>`).join('');

      if (currentFolderBrowserData) {
        renderFolderBrowser(currentFolderBrowserData);
      }
    }

    function addSelectedFolder(path) {
      selectedFolders = dedupeFolders([...selectedFolders, path]);
      renderSelectedFolders();
    }

    function addVisibleFolders(paths) {
      selectedFolders = dedupeFolders([...selectedFolders, ...paths]);
      renderSelectedFolders();
    }

    function removeSelectedFolder(index) {
      selectedFolders.splice(index, 1);
      renderSelectedFolders();
    }

    function clearSelectedFolders() {
      selectedFolders = [];
      renderSelectedFolders();
    }

    function onPhotoRootChanged() {
      updatePhotoRootDisplay();
      if (selectedFolders.length === 0) {
        const summary = document.getElementById('statusSelectedFolderSummary');
        if (summary) {
          summary.textContent = getEffectivePhotoRoot();
        }
      }
    }

    function findPresetById(profileId) {
      return profilePresets.find(preset => preset.id === profileId) || null;
    }

    function findFallbackPresetById(profileId) {
      return fallbackPresets.find(preset => preset.id === profileId) || null;
    }

    function determineProfilePreset() {
      return profilePresets.find(preset =>
        preset.primaryOllamaUrl.trim().toLowerCase() === textValue('primaryOllamaUrl').trim().toLowerCase()
        && preset.primaryModel.trim().toLowerCase() === textValue('primaryModel').trim().toLowerCase()
        && Number(preset.maxImageSize) === Number(numericValue('maxImageSize') ?? 0)
        && Boolean(preset.fallbackEnabled) === checkboxValue('fallbackEnabled')
        && preset.fallbackOllamaUrl.trim().toLowerCase() === textValue('fallbackOllamaUrl').trim().toLowerCase()
        && preset.fallbackModel.trim().toLowerCase() === textValue('fallbackModel').trim().toLowerCase()
        && Number(preset.fallbackMaxImageSize) === Number(numericValue('fallbackMaxImageSize') ?? 0)
      ) || null;
    }

    function refreshProfileSummary() {
      const matched = determineProfilePreset();
      const presetSelect = document.getElementById('profilePreset');
      const summary = document.getElementById('profileSummary');
      if (matched) {
        presetSelect.value = matched.id;
        summary.textContent = matched.summary;
      } else {
        presetSelect.value = 'Custom';
        summary.textContent = 'Custom | Target: edit the model fields below as needed.';
      }

      refreshFallbackPresetSummary();
    }

    function applyProfilePreset(profileId) {
      if (profileId === 'Custom') {
        refreshProfileSummary();
        return;
      }

      const preset = findPresetById(profileId);
      if (!preset) {
        refreshProfileSummary();
        return;
      }

      document.getElementById('primaryOllamaUrl').value = preset.primaryOllamaUrl;
      setSelectValue('primaryModel', preset.primaryModel);
      document.getElementById('maxImageSize').value = preset.maxImageSize;
      document.getElementById('fallbackEnabled').checked = preset.fallbackEnabled;
      document.getElementById('fallbackOllamaUrl').value = preset.fallbackOllamaUrl;
      setSelectValue('fallbackModel', preset.fallbackModel);
      document.getElementById('fallbackMaxImageSize').value = preset.fallbackMaxImageSize;
      document.getElementById('profileSummary').textContent = preset.summary;
      document.getElementById('profilePreset').value = preset.id;
      updateFallbackControlState();
      refreshFallbackPresetSummary();
      scheduleModelRefresh('primary');
      scheduleModelRefresh('fallback');
    }

    function determineFallbackPreset() {
      return fallbackPresets.find(preset =>
        preset.fallbackOllamaUrl.trim().toLowerCase() === textValue('fallbackOllamaUrl').trim().toLowerCase()
        && preset.fallbackModel.trim().toLowerCase() === textValue('fallbackModel').trim().toLowerCase()
        && Number(preset.fallbackMaxImageSize) === Number(numericValue('fallbackMaxImageSize') ?? 0)
      ) || null;
    }

    function refreshFallbackPresetSummary() {
      const preset = determineFallbackPreset();
      const presetSelect = document.getElementById('fallbackPreset');
      const summary = document.getElementById('fallbackPresetSummary');
      if (preset) {
        presetSelect.value = preset.id;
        summary.textContent = preset.summary;
      } else {
        presetSelect.value = 'Custom';
        summary.textContent = 'Custom fallback | Target: edit fallback URL, model, and Max Image Size as needed.';
      }
    }

    function applyFallbackPreset(profileId) {
      if (profileId === 'Custom') {
        refreshFallbackPresetSummary();
        return;
      }

      const preset = findFallbackPresetById(profileId);
      if (!preset) {
        refreshFallbackPresetSummary();
        return;
      }

      document.getElementById('fallbackEnabled').checked = true;
      document.getElementById('fallbackOllamaUrl').value = preset.fallbackOllamaUrl;
      setSelectValue('fallbackModel', preset.fallbackModel);
      document.getElementById('fallbackMaxImageSize').value = preset.fallbackMaxImageSize;
      updateFallbackControlState();
      refreshFallbackPresetSummary();
      refreshProfileSummary();
      scheduleModelRefresh('fallback');
    }

    function setSelectOptions(selectId, models, preferredModel) {
      const select = document.getElementById(selectId);
      const currentValue = (select.value || '').trim();
      const uniqueModels = [...new Set((models || []).map(model => (model || '').trim()).filter(Boolean))];
      const fallbackValue = (preferredModel || '').trim() || currentValue;
      if (fallbackValue && !uniqueModels.some(model => model.toLowerCase() === fallbackValue.toLowerCase())) {
        uniqueModels.unshift(fallbackValue);
      }

      select.innerHTML = uniqueModels.map(model => `<option value="${escapeHtml(model)}">${escapeHtml(model)}</option>`).join('');

      const chosenValue = uniqueModels.find(model => model.toLowerCase() === currentValue.toLowerCase())
        || uniqueModels.find(model => model.toLowerCase() === fallbackValue.toLowerCase())
        || uniqueModels[0]
        || '';

      select.value = chosenValue;
    }

    function setSelectValue(selectId, value) {
      setSelectOptions(selectId, [value], value);
    }

    function updateFallbackControlState() {
      const enabled = checkboxValue('fallbackEnabled');
      ['fallbackPreset', 'fallbackOllamaUrl', 'fallbackModel', 'fallbackMaxImageSize'].forEach(id => {
        document.getElementById(id).disabled = !enabled;
      });

      const status = document.getElementById('fallbackModelStatus');
      status.textContent = enabled
        ? 'When fallback is enabled, this dropdown reloads from the fallback Ollama URL.'
        : 'Fallback disabled.';
    }

    async function refreshModelsForTarget(target, showErrors = false) {
      const isFallback = target === 'fallback';
      if (isFallback && !checkboxValue('fallbackEnabled')) {
        return;
      }

      const urlFieldId = isFallback ? 'fallbackOllamaUrl' : 'primaryOllamaUrl';
      const modelFieldId = isFallback ? 'fallbackModel' : 'primaryModel';
      const statusFieldId = isFallback ? 'fallbackModelStatus' : 'primaryModelStatus';
      const defaultModel = isFallback
        ? (findFallbackPresetById(document.getElementById('fallbackPreset').value)?.fallbackModel || textValue('fallbackModel'))
        : textValue('primaryModel');
      const baseUrl = textValue(urlFieldId).trim();
      const status = document.getElementById(statusFieldId);
      if (!baseUrl) {
        status.textContent = `Enter a ${isFallback ? 'fallback' : 'primary'} Ollama URL to load models.`;
        return;
      }

      status.textContent = `Loading models from ${baseUrl}...`;

      try {
        const response = await fetch(`/api/models?baseUrl=${encodeURIComponent(baseUrl)}&target=${isFallback ? 'fallback' : 'primary'}`);
        if (!response.ok) {
          const message = await response.text();
          status.textContent = `Unable to load models from ${baseUrl}. ${message}`;
          if (showErrors) alert(message);
          return;
        }

        const data = await response.json();
        setSelectOptions(modelFieldId, data.models || [], defaultModel);
        status.textContent = `${data.models.length} model(s) loaded from ${data.baseUrl}.`;
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        status.textContent = `Unable to load models from ${baseUrl}. ${message}`;
        if (showErrors) alert(message);
        return;
      }

      if (isFallback) {
        refreshFallbackPresetSummary();
      }
      refreshProfileSummary();
    }

    function scheduleModelRefresh(target) {
      clearTimeout(scheduleModelRefresh.timers[target]);
      scheduleModelRefresh.timers[target] = setTimeout(() => refreshModelsForTarget(target, false), 250);
    }
    scheduleModelRefresh.timers = { primary: null, fallback: null };

    async function saveSettings() {
      const response = await fetch('/api/settings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          photoRoot: textValue('photoRoot'),
          configRoot: textValue('configRoot'),
          logRoot: textValue('logRoot'),
          recursive: checkboxValue('recursive'),
          force: checkboxValue('force'),
          writeJson: checkboxValue('writeJson'),
          writeXmp: checkboxValue('writeXmp'),
          addTags: checkboxValue('addTags'),
          overwriteSidecars: checkboxValue('overwriteSidecars'),
          limit: numericValue('limit'),
          primaryOllamaUrl: textValue('primaryOllamaUrl'),
          primaryModel: textValue('primaryModel'),
          maxImageSize: numericValue('maxImageSize') ?? 0,
          fallbackEnabled: checkboxValue('fallbackEnabled'),
          fallbackOllamaUrl: textValue('fallbackOllamaUrl'),
          fallbackModel: textValue('fallbackModel'),
          fallbackMaxImageSize: numericValue('fallbackMaxImageSize') ?? 0,
          syncImmich: checkboxValue('syncImmich'),
          immichBaseUrl: textValue('immichBaseUrl'),
          immichApiKey: textValue('immichApiKey')
        })
      });
      if (!response.ok) alert(await response.text());
      location.reload();
    }

    function renderFolderBrowser(data) {
      currentFolderBrowserData = data;
      currentBrowsePath = data.path;
      const browser = document.getElementById('folderBrowser');
      const lines = [];
      lines.push(`<div class="folder-browser-toolbar">`);
      lines.push(`<strong>${escapeHtml(data.path)}</strong>`);
      lines.push(`<button type="button" onclick="addSelectedFolder('${escapeJs(data.path)}')">Add this folder</button>`);
      if (data.directories.length > 0) {
        lines.push(`<button type="button" onclick="addVisibleFolders(${JSON.stringify(data.directories)})">Add all visible folders</button>`);
      }
      if (data.parent) lines.push(`<button type="button" onclick="browseFolders('${escapeJs(data.parent)}')">.. parent</button>`);
      lines.push(`</div>`);
      if (data.directories.length === 0) lines.push('<p>No child folders.</p>');
      for (const directory of data.directories) {
        const label = directory.split('/').filter(Boolean).pop() || directory;
        const isSelected = selectedFolders.includes(directory);
        lines.push(`<div class="folder-row"><button type="button" onclick="browseFolders('${escapeJs(directory)}')">Browse</button><span>${escapeHtml(label)}${isSelected ? ' • selected' : ''}</span><button type="button" onclick="selectFolder('${escapeJs(directory)}')">Add</button></div>`);
      }
      browser.innerHTML = lines.join('');
    }

    async function browseFolders(path) {
      const browser = document.getElementById('folderBrowser');
      browser.textContent = 'Loading folders...';
      const response = await fetch('/api/folders?path=' + encodeURIComponent(path));
      if (!response.ok) {
        browser.textContent = await response.text();
        return;
      }
      renderFolderBrowser(await response.json());
    }

    function selectFolder(path) {
      addSelectedFolder(path);
    }

    function escapeHtml(value) {
      return value.replace(/[&<>"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[character]));
    }

    function escapeJs(value) {
      return value.replace(/\\/g, '\\\\').replace(/'/g, "\\'");
    }

    async function startScan(endpoint) {
      const folderPath = getEffectivePhotoRoot();
      const limitRaw = document.getElementById('limit').value;
      const limit = limitRaw ? Number(limitRaw) : null;
      const folderPaths = dedupeFolders(selectedFolders);
      persistSelectedFolders(folderPaths, true);
      const response = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ folderPath, folderPaths, limit })
      });
      if (!response.ok) alert(await response.text());
      location.reload();
    }

    async function togglePauseResume() {
      const endpoint = {{(status.IsPaused ? "'/api/resume'" : "'/api/pause'")}};
      const response = await fetch(endpoint, { method: 'POST' });
      if (!response.ok) alert(await response.text());
      location.reload();
    }

    async function cancelScan() {
      const response = await fetch('/api/cancel', { method: 'POST' });
      if (!response.ok) alert(await response.text());
      location.reload();
    }

    document.getElementById('photoRoot').addEventListener('input', onPhotoRootChanged);
    document.getElementById('fallbackEnabled').addEventListener('change', () => {
      updateFallbackControlState();
      refreshProfileSummary();
      refreshFallbackPresetSummary();
      if (checkboxValue('fallbackEnabled')) {
        scheduleModelRefresh('fallback');
      }
    });
    document.getElementById('primaryOllamaUrl').addEventListener('change', () => scheduleModelRefresh('primary'));
    document.getElementById('primaryOllamaUrl').addEventListener('blur', () => scheduleModelRefresh('primary'));
    document.getElementById('fallbackOllamaUrl').addEventListener('change', () => scheduleModelRefresh('fallback'));
    document.getElementById('fallbackOllamaUrl').addEventListener('blur', () => scheduleModelRefresh('fallback'));
    ['primaryOllamaUrl', 'primaryModel', 'maxImageSize', 'fallbackEnabled', 'fallbackOllamaUrl', 'fallbackModel', 'fallbackMaxImageSize']
      .forEach(id => document.getElementById(id).addEventListener(id === 'fallbackEnabled' ? 'change' : 'input', refreshProfileSummary));

    setSelectValue('primaryModel', '{{Encode(settings.PrimaryModel)}}');
    setSelectValue('fallbackModel', '{{Encode(settings.FallbackModel)}}');
    updateFallbackControlState();
    renderSelectedFolders();
    refreshProfileSummary();
    refreshFallbackPresetSummary();
    if (initialProfilePresetId !== 'Custom') {
      document.getElementById('profilePreset').value = initialProfilePresetId;
    }
    scheduleModelRefresh('primary');
    if (checkboxValue('fallbackEnabled')) {
      scheduleModelRefresh('fallback');
    }
    browseFolders(currentBrowsePath || getEffectivePhotoRoot());
    if ({{(status.IsRunning ? "true" : "false")}}) {
      setTimeout(() => location.reload(), 10000);
    }
  </script>
</body>
</html>
""";
}

static string RenderProgress(PhotoAiScanProgress? progress)
{
    if (progress?.Snapshot is not { } snapshot)
    {
        return string.Empty;
    }

    string Encode(string? value) => HtmlEncoder.Default.Encode(value ?? string.Empty);
    string percentDisplay = snapshot.ProgressFraction?.ToString("0") ?? "0";
    int progressValue = (int)Math.Round(snapshot.ProgressFraction ?? 0, MidpointRounding.AwayFromZero);
    string currentFile = string.IsNullOrWhiteSpace(progress.ImagePath) ? "Waiting for next file..." : Encode(progress.ImagePath);
    return $"""
      <div class="status-progress">
        <div><strong>{Encode(snapshot.Phase)}</strong></div>
        <progress id="scanProgress" max="100" value="{progressValue}"></progress>
        <div class="status-grid">
          <div class="status-metric"><div class="label">Files</div><div class="value">{snapshot.FilesFinished} / {snapshot.TotalFiles?.ToString() ?? "?"} ({percentDisplay}%)</div></div>
          <div class="status-metric"><div class="label">Elapsed</div><div class="value">{FormatDuration(snapshot.Elapsed)}</div></div>
          <div class="status-metric"><div class="label">Remaining</div><div class="value">{Encode(snapshot.EstimatedRemainingDisplay)}</div></div>
          <div class="status-metric"><div class="label">ETA</div><div class="value">{Encode(snapshot.EstimatedFinishTimeDisplay)}</div></div>
          <div class="status-metric"><div class="label">Retry</div><div class="value">{snapshot.PrimaryRetryAttempts}</div></div>
          <div class="status-metric"><div class="label">Fallback</div><div class="value">{snapshot.FallbackAttempts}</div></div>
          <div class="status-metric"><div class="label">Current file</div><div class="value">{currentFile}</div></div>
          <div class="status-metric"><div class="label">Current file event</div><div class="value">{Encode(progress.EventName)} — {Encode(progress.Message)}</div></div>
        </div>
      </div>
""";
}

static PhotoAiModelProfile BuildProfileFromSettings(ImmichTaggerSettings settings)
{
    return new PhotoAiModelProfile
    {
        ProfileId = PhotoAiModelProfileId.Custom,
        DisplayName = "Custom",
        OllamaBaseUrl = settings.PrimaryOllamaUrl,
        Model = settings.PrimaryModel,
        MaxImageDimensionPixels = settings.MaxImageSize,
        ModelPreference = settings.FallbackEnabled
            ? PhotoAiModelPreference.QwenPcWithUnraidFallback
            : PhotoAiModelPreference.PrimaryOnly,
        FallbackOllamaBaseUrl = settings.FallbackOllamaUrl,
        FallbackModel = settings.FallbackModel,
        FallbackMaxImageDimensionPixels = settings.FallbackMaxImageSize
    };
}

static string FormatDuration(TimeSpan duration)
{
    if (duration < TimeSpan.Zero)
    {
        duration = TimeSpan.Zero;
    }

    return duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
        : $"{duration.Minutes:00}:{duration.Seconds:00}";
}

static string? ExtractFirstLogPath(string? runLogPath)
{
    return runLogPath?
        .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => line.StartsWith("Anomaly log:", StringComparison.OrdinalIgnoreCase))
        .Select(line => line["Anomaly log:".Length..].Trim())
        .FirstOrDefault(path => !path.StartsWith("(", StringComparison.Ordinal));
}

public sealed record ImmichTaggerSettingsUpdate(
    string? PhotoRoot,
    string? ConfigRoot,
    string? LogRoot,
    string? DefaultFolderPath,
    bool Recursive,
    bool Force,
    bool WriteJson,
    bool WriteXmp,
    bool AddTags,
    bool OverwriteSidecars,
    int? Limit,
    string? PrimaryOllamaUrl,
    string? PrimaryModel,
    int MaxImageSize,
    bool FallbackEnabled,
    string? FallbackOllamaUrl,
    string? FallbackModel,
    int FallbackMaxImageSize,
    bool SyncImmich,
    string? ImmichBaseUrl,
    string? ImmichApiKey);

public sealed record FolderBrowseResponse(string Path, string? Parent, IReadOnlyList<string> Directories);

public sealed record OllamaModelsResponse(string BaseUrl, IReadOnlyList<string> Models);
