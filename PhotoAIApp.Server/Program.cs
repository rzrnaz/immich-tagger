using ImmichTagger.Server;
using PhotoAIApp.Core;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "IMMICH_TAGGER__");
builder.Services.Configure<ImmichTaggerSettings>(builder.Configuration);
builder.Services.AddSingleton(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    return configuration.Get<ImmichTaggerSettings>() ?? new ImmichTaggerSettings();
});
builder.Services.AddSingleton<ScanJobService>();

var app = builder.Build();

app.MapGet("/", (ImmichTaggerSettings settings, ScanJobService jobs) => Results.Content(RenderHome(settings, jobs.Status), "text/html"));
app.MapGet("/api/settings", (ImmichTaggerSettings settings) => Results.Ok(settings));
app.MapGet("/api/status", (ScanJobService jobs) => Results.Ok(jobs.Status));

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

static string RenderHome(ImmichTaggerSettings settings, ScanJobStatus status)
{
    string folder = HtmlEncoder.Default.Encode(settings.DefaultFolderPath);
    string message = HtmlEncoder.Default.Encode(status.Message);
    string error = HtmlEncoder.Default.Encode(status.Error ?? string.Empty);
    string recentLog = string.Join("\n", status.RecentLogLines.Select(HtmlEncoder.Default.Encode));
    string running = status.IsRunning ? "Running" : "Idle";
    string started = status.StartedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "n/a";
    string finished = status.FinishedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "n/a";

    return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Immich Tagger</title>
  <style>
    body { font-family: system-ui, Segoe UI, sans-serif; background: #f7f1e8; color: #111; margin: 0; }
    main { max-width: 980px; margin: 0 auto; padding: 32px; }
    .card { background: #fffaf2; border: 1px solid #d8c3a5; border-radius: 16px; padding: 20px; margin: 16px 0; box-shadow: 0 2px 10px #00000012; }
    label { display: block; font-weight: 650; margin-bottom: 6px; }
    input { box-sizing: border-box; width: 100%; padding: 10px; border-radius: 8px; border: 1px solid #c9b79c; font-size: 1rem; }
    button { border: 0; border-radius: 10px; padding: 11px 18px; margin: 8px 8px 0 0; color: #f7f1e8; background: #2f75b5; font-weight: 700; cursor: pointer; }
    button.stop { background: #b64234; }
    dl { display: grid; grid-template-columns: 180px 1fr; gap: 8px; }
    dt { font-weight: 700; }
    pre { white-space: pre-wrap; background: #211f1c; color: #f7f1e8; border-radius: 12px; padding: 16px; min-height: 120px; }
    .error { color: #a1261d; font-weight: 700; }
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
      <label for="limit">Limit, optional</label>
      <input id="limit" type="number" min="0" placeholder="0 = no limit">
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
        <dt>Primary Ollama</dt><dd>{{HtmlEncoder.Default.Encode(settings.PrimaryOllamaUrl)}}</dd>
        <dt>Primary Model</dt><dd>{{HtmlEncoder.Default.Encode(settings.PrimaryModel)}}</dd>
        <dt>Max Image Size</dt><dd>{{settings.MaxImageSize}}</dd>
        <dt>Fallback</dt><dd>{{settings.FallbackEnabled}} / {{HtmlEncoder.Default.Encode(settings.FallbackModel)}}</dd>
      </dl>
      {{(string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class=\"error\">{error}</p>")}}
    </section>

    <section class="card">
      <h2>Recent log</h2>
      <pre>{{recentLog}}</pre>
    </section>
  </main>

  <script>
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

    setTimeout(() => location.reload(), 10000);
  </script>
</body>
</html>
""";
}
