using ImmichTagger.Server;
using PhotoAIApp.Core;
using System.Text.Encodings.Web;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "IMMICH_TAGGER__");
ImmichTaggerSettings initialSettings = builder.Configuration.Get<ImmichTaggerSettings>() ?? new ImmichTaggerSettings();
LoadPersistedSettings(initialSettings);
builder.Services.AddSingleton(initialSettings);
builder.Services.AddSingleton<ScanJobService>();

var app = builder.Build();

app.MapGet("/", (ImmichTaggerSettings settings, ScanJobService jobs) => Results.Content(RenderHome(settings, jobs.Status), "text/html"));
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
    if (!ValidatePathForRead(requestedPath, [settings.PhotoRoot], out string fullPath, out string? error))
    {
        return Results.BadRequest(new { message = error });
    }

    if (!Directory.Exists(fullPath))
    {
        return Results.NotFound(new { message = $"Folder does not exist: {fullPath}" });
    }

    string[] directories = Directory.EnumerateDirectories(fullPath)
        .Where(directory => !IsHiddenOrInternalFolder(directory))
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

app.MapPost("/api/dry-run", (ImmichTaggerSettings settings, ScanJobService jobs, ScanStartRequest request) =>
{
    return jobs.TryStart(settings, request, dryRun: true, out string message)
        ? Results.Accepted("/api/status", new { message })
        : Results.Conflict(new { message });
});

app.MapPost("/api/run", (ImmichTaggerSettings settings, ScanJobService jobs, ScanStartRequest request) =>
{
    return jobs.TryStart(settings, request, dryRun: false, out string message)
        ? Results.Accepted("/api/status", new { message })
        : Results.Conflict(new { message });
});

app.MapPost("/api/cancel", (ScanJobService jobs) =>
{
    return jobs.Cancel(out string message)
        ? Results.Accepted("/api/status", new { message })
        : Results.NotFound(new { message });
});

app.Run();

static void ApplySettingsUpdate(ImmichTaggerSettings settings, ImmichTaggerSettingsUpdate request)
{
    settings.PhotoRoot = CleanPath(request.PhotoRoot, settings.PhotoRoot);
    settings.ConfigRoot = CleanPath(request.ConfigRoot, settings.ConfigRoot);
    settings.LogRoot = CleanPath(request.LogRoot, settings.LogRoot);
    settings.DefaultFolderPath = CleanPath(request.DefaultFolderPath, settings.DefaultFolderPath);
    settings.Recursive = request.Recursive;
    settings.Force = request.Force;
    settings.WriteJson = request.WriteJson;
    settings.WriteXmp = request.WriteXmp;
    settings.AddTags = request.AddTags;
    settings.DryRunDefault = request.DryRunDefault;
    settings.OverwriteSidecars = request.OverwriteSidecars;
    settings.Limit = request.Limit is > 0 ? request.Limit : null;
    settings.PrimaryOllamaUrl = CleanText(request.PrimaryOllamaUrl, settings.PrimaryOllamaUrl);
    settings.PrimaryModel = CleanText(request.PrimaryModel, settings.PrimaryModel);
    settings.MaxImageSize = Math.Max(0, request.MaxImageSize);
    settings.FallbackEnabled = request.FallbackEnabled;
    settings.FallbackOllamaUrl = CleanText(request.FallbackOllamaUrl, settings.FallbackOllamaUrl);
    settings.FallbackModel = CleanText(request.FallbackModel, settings.FallbackModel);
    settings.FallbackMaxImageSize = Math.Max(0, request.FallbackMaxImageSize);
}

static string CleanText(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

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

static bool IsHiddenOrInternalFolder(string directory)
{
    string name = Path.GetFileName(directory);
    return name.StartsWith(".", StringComparison.Ordinal)
        || string.Equals(name, "@eaDir", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, ".Recycle.Bin", StringComparison.OrdinalIgnoreCase);
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
    target.PrimaryOllamaUrl = source.PrimaryOllamaUrl;
    target.PrimaryModel = source.PrimaryModel;
    target.MaxImageSize = source.MaxImageSize;
    target.FallbackEnabled = source.FallbackEnabled;
    target.FallbackOllamaUrl = source.FallbackOllamaUrl;
    target.FallbackModel = source.FallbackModel;
    target.FallbackMaxImageSize = source.FallbackMaxImageSize;
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
    string folder = Encode(string.IsNullOrWhiteSpace(status.FolderPath) ? settings.DefaultFolderPath : status.FolderPath);
    string message = Encode(status.Message);
    string error = Encode(status.Error ?? string.Empty);
    string recentLog = string.Join("\n", status.RecentLogLines.Select(HtmlEncoder.Default.Encode));
    string running = status.IsRunning ? "Running" : "Idle";
    string started = status.StartedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "n/a";
    string finished = status.FinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "n/a";
    string progress = RenderProgress(status.LastProgress);
    string summary = status.Summary is null ? string.Empty : $"<pre>{Encode(status.Summary.PlainText)}</pre>";
    string? runLogPath = ExtractFirstLogPath(status.Summary?.PlainText);
    string openLogLink = string.IsNullOrWhiteSpace(runLogPath)
        ? "<span>No run log available yet.</span>"
        : $"<a id=\"openLogLink\" href=\"/api/log?path={Uri.EscapeDataString(runLogPath)}\" target=\"_blank\">Open Log</a>";

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Immich Tagger</title>
  <style>
    body { font-family: system-ui, Segoe UI, sans-serif; background: #f7f1e8; color: #111; margin: 0; }
    main { max-width: 1180px; margin: 0 auto; padding: 32px; }
    .card { background: #fffaf2; border: 1px solid #d8c3a5; border-radius: 16px; padding: 20px; margin: 16px 0; box-shadow: 0 2px 10px #00000012; }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(270px, 1fr)); gap: 16px; }
    label { display: block; font-weight: 650; margin: 12px 0 6px; }
    input { box-sizing: border-box; width: 100%; padding: 10px; border-radius: 8px; border: 1px solid #c9b79c; font-size: 1rem; }
    input[type="checkbox"] { width: auto; margin-right: 8px; }
    button, a.button { border: 0; border-radius: 10px; padding: 11px 18px; margin: 8px 8px 0 0; color: #f7f1e8; background: #2f75b5; font-weight: 700; cursor: pointer; text-decoration: none; display: inline-block; }
    button.stop { background: #b64234; }
    button.secondary { background: #9b6f3d; }
    dl { display: grid; grid-template-columns: 180px 1fr; gap: 8px; }
    dt { font-weight: 700; }
    pre { white-space: pre-wrap; background: #211f1c; color: #f7f1e8; border-radius: 12px; padding: 16px; min-height: 100px; overflow: auto; }
    .error { color: #a1261d; font-weight: 700; }
    .folder-list button { display: block; width: 100%; text-align: left; background: #efe2cf; color: #111; margin: 6px 0; }
    .muted { color: #685f52; }
    .switches label { font-weight: 500; }
  </style>
</head>
<body>
  <main>
    <h1>Immich Tagger</h1>
    <p>AI-powered tags, descriptions, and XMP sidecars for Immich photo libraries.</p>

    <section class="card">
      <h2>Start a scan</h2>
      <label for="folderPath">Folder under /photos</label>
      <input id="folderPath" value="{{folder}}">
      <div>
        <button class="secondary" onclick="browseFolders(document.getElementById('folderPath').value)">Browse this folder</button>
        <button class="secondary" onclick="browseFolders('{{Encode(settings.PhotoRoot)}}')">Browse /photos</button>
      </div>
      <div id="folderBrowser" class="folder-list muted">Folder browser will appear here.</div>
      <label for="limit">Limit, optional</label>
      <input id="limit" type="number" min="0" value="{{settings.Limit?.ToString() ?? string.Empty}}" placeholder="0 = no limit">
      <button onclick="startScan('/api/dry-run')">Dry Run</button>
      <button onclick="startScan('/api/run')">Run Scan</button>
      <button class="stop" onclick="cancelScan()">Stop</button>
    </section>

    <section class="card">
      <h2>Status: {{running}}</h2>
      <dl>
        <dt>Message</dt><dd>{{message}}</dd>
        <dt>Started</dt><dd>{{started}}</dd>
        <dt>Finished</dt><dd>{{finished}}</dd>
        <dt>Open Log</dt><dd>{{openLogLink}}</dd>
        <dt>Primary Ollama</dt><dd>{{Encode(settings.PrimaryOllamaUrl)}}</dd>
        <dt>Primary Model</dt><dd>{{Encode(settings.PrimaryModel)}}</dd>
        <dt>Max Image Size</dt><dd>{{settings.MaxImageSize}}</dd>
        <dt>Fallback</dt><dd>{{settings.FallbackEnabled}} / {{Encode(settings.FallbackModel)}}</dd>
      </dl>
      {{progress}}
      {{(string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class=\"error\">{error}</p>")}}
      {{summary}}
    </section>

    <section class="card">
      <h2>Settings</h2>
      <p class="muted">These values start from the Unraid template/environment defaults. Saving here writes /config/immich-tagger-settings.json so GUI edits survive container restarts. Update the Unraid template/container variables when you want to change the first-run defaults.</p>
      <div class="grid">
        <div>
          <label for="photoRoot">Photo root</label>
          <input id="photoRoot" value="{{Encode(settings.PhotoRoot)}}">
          <label for="configRoot">Config root</label>
          <input id="configRoot" value="{{Encode(settings.ConfigRoot)}}">
          <label for="logRoot">Log root</label>
          <input id="logRoot" value="{{Encode(settings.LogRoot)}}">
          <label for="defaultFolderPath">Default folder</label>
          <input id="defaultFolderPath" value="{{Encode(settings.DefaultFolderPath)}}">
        </div>
        <div>
          <label for="primaryOllamaUrl">Primary Ollama URL</label>
          <input id="primaryOllamaUrl" value="{{Encode(settings.PrimaryOllamaUrl)}}">
          <label for="primaryModel">Primary model</label>
          <input id="primaryModel" value="{{Encode(settings.PrimaryModel)}}">
          <label for="maxImageSize">Max Image Size</label>
          <input id="maxImageSize" type="number" min="0" value="{{settings.MaxImageSize}}">
          <label for="fallbackOllamaUrl">Fallback Ollama URL</label>
          <input id="fallbackOllamaUrl" value="{{Encode(settings.FallbackOllamaUrl)}}">
          <label for="fallbackModel">Fallback model</label>
          <input id="fallbackModel" value="{{Encode(settings.FallbackModel)}}">
          <label for="fallbackMaxImageSize">Fallback Max Image Size</label>
          <input id="fallbackMaxImageSize" type="number" min="0" value="{{settings.FallbackMaxImageSize}}">
        </div>
        <div class="switches">
          <label><input id="recursive" type="checkbox" {{Checked(settings.Recursive)}}>Subfolders</label>
          <label><input id="force" type="checkbox" {{Checked(settings.Force)}}>Scan existing PhotoAI sidecars</label>
          <label><input id="writeJson" type="checkbox" {{Checked(settings.WriteJson)}}>Write JSON diagnostics</label>
          <label><input id="writeXmp" type="checkbox" {{Checked(settings.WriteXmp)}}>Write Immich XMP sidecars</label>
          <label><input id="addTags" type="checkbox" {{Checked(settings.AddTags)}}>Add tags</label>
          <label><input id="dryRunDefault" type="checkbox" {{Checked(settings.DryRunDefault)}}>Dry run by default</label>
          <label><input id="overwriteSidecars" type="checkbox" {{Checked(settings.OverwriteSidecars)}}>Overwrite sidecars</label>
          <label><input id="fallbackEnabled" type="checkbox" {{Checked(settings.FallbackEnabled)}}>Fallback enabled</label>
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
    const numericValue = (id) => {
      const raw = document.getElementById(id).value;
      return raw === '' ? null : Number(raw);
    };

    const checkboxValue = (id) => document.getElementById(id).checked;
    const textValue = (id) => document.getElementById(id).value;

    async function saveSettings() {
      const response = await fetch('/api/settings', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          photoRoot: textValue('photoRoot'),
          configRoot: textValue('configRoot'),
          logRoot: textValue('logRoot'),
          defaultFolderPath: textValue('defaultFolderPath'),
          recursive: checkboxValue('recursive'),
          force: checkboxValue('force'),
          writeJson: checkboxValue('writeJson'),
          writeXmp: checkboxValue('writeXmp'),
          addTags: checkboxValue('addTags'),
          dryRunDefault: checkboxValue('dryRunDefault'),
          overwriteSidecars: checkboxValue('overwriteSidecars'),
          limit: numericValue('limit'),
          primaryOllamaUrl: textValue('primaryOllamaUrl'),
          primaryModel: textValue('primaryModel'),
          maxImageSize: numericValue('maxImageSize') ?? 0,
          fallbackEnabled: checkboxValue('fallbackEnabled'),
          fallbackOllamaUrl: textValue('fallbackOllamaUrl'),
          fallbackModel: textValue('fallbackModel'),
          fallbackMaxImageSize: numericValue('fallbackMaxImageSize') ?? 0
        })
      });
      if (!response.ok) alert(await response.text());
      location.reload();
    }

    async function browseFolders(path) {
      const browser = document.getElementById('folderBrowser');
      browser.textContent = 'Loading folders...';
      const response = await fetch('/api/folders?path=' + encodeURIComponent(path));
      if (!response.ok) {
        browser.textContent = await response.text();
        return;
      }
      const data = await response.json();
      const lines = [];
      lines.push(`<p><strong>${data.path}</strong></p>`);
      if (data.parent) lines.push(`<button type="button" onclick="browseFolders('${escapeJs(data.parent)}')">.. parent</button>`);
      if (data.directories.length === 0) lines.push('<p>No child folders.</p>');
      for (const directory of data.directories) {
        const label = directory.split('/').filter(Boolean).pop() || directory;
        lines.push(`<button type="button" onclick="selectFolder('${escapeJs(directory)}')">${escapeHtml(label)}</button>`);
      }
      browser.innerHTML = lines.join('');
    }

    function selectFolder(path) {
      document.getElementById('folderPath').value = path;
      document.getElementById('defaultFolderPath').value = path;
      browseFolders(path);
    }

    function escapeHtml(value) {
      return value.replace(/[&<>"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[character]));
    }

    function escapeJs(value) {
      return value.replace(/\\/g, '\\\\').replace(/'/g, "\\'");
    }

    async function startScan(endpoint) {
      const folderPath = document.getElementById('folderPath').value;
      const limitRaw = document.getElementById('limit').value;
      const limit = limitRaw ? Number(limitRaw) : null;
      const response = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ folderPath, limit })
      });
      if (!response.ok) alert(await response.text());
      location.reload();
    }

    async function cancelScan() {
      const response = await fetch('/api/cancel', { method: 'POST' });
      if (!response.ok) alert(await response.text());
      location.reload();
    }

    browseFolders(document.getElementById('folderPath').value);
    setTimeout(() => location.reload(), 10000);
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
    string percent = snapshot.ProgressFraction?.ToString("0") ?? "n/a";
    return $"""
      <dl>
        <dt>Phase</dt><dd>{Encode(snapshot.Phase)}</dd>
        <dt>Files</dt><dd>{snapshot.FilesFinished} / {snapshot.TotalFiles?.ToString() ?? "?"} ({percent}%)</dd>
        <dt>Remaining</dt><dd>{Encode(snapshot.EstimatedRemainingDisplay)}</dd>
        <dt>ETA</dt><dd>{Encode(snapshot.EstimatedFinishTimeDisplay)}</dd>
        <dt>Retry</dt><dd>{snapshot.PrimaryRetryAttempts}</dd>
        <dt>Fallback</dt><dd>{snapshot.FallbackAttempts}</dd>
      </dl>
""";
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
    bool DryRunDefault,
    bool OverwriteSidecars,
    int? Limit,
    string? PrimaryOllamaUrl,
    string? PrimaryModel,
    int MaxImageSize,
    bool FallbackEnabled,
    string? FallbackOllamaUrl,
    string? FallbackModel,
    int FallbackMaxImageSize);

public sealed record FolderBrowseResponse(string Path, string? Parent, IReadOnlyList<string> Directories);
