using PhotoAIApp.Core;

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}. Expected '{expected}', actual '{actual}'.");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var qwenPlan = PhotoAiScanner.BuildModelEndpointPlan(new PhotoAiScanOptions
{
    ModelPreference = PhotoAiModelPreference.QwenPcWithUnraidFallback,
    OllamaBaseUrl = "http://pc:11434",
    Model = "qwen2.5vl:3b",
    FallbackOllamaBaseUrl = "http://unraid:11434",
    FallbackModel = "minicpm-v:latest"
});

AssertEqual(2, qwenPlan.Length, "Qwen/PC preference should include fallback endpoint");
AssertEqual("http://pc:11434", qwenPlan[0].OllamaBaseUrl, "Primary endpoint URL should be PC URL");
AssertEqual("qwen2.5vl:3b", qwenPlan[0].Model, "Primary endpoint model should be Qwen");
AssertEqual(false, qwenPlan[0].IsFallback, "Primary endpoint should not be fallback");
AssertEqual("http://unraid:11434", qwenPlan[1].OllamaBaseUrl, "Fallback endpoint URL should be Unraid URL");
AssertEqual("minicpm-v:latest", qwenPlan[1].Model, "Fallback endpoint model should be MiniCPM-V");
AssertEqual(true, qwenPlan[1].IsFallback, "Second endpoint should be fallback");

var unraidPlan = PhotoAiScanner.BuildModelEndpointPlan(new PhotoAiScanOptions
{
    ModelPreference = PhotoAiModelPreference.UnraidMiniCpmOnly,
    OllamaBaseUrl = "http://pc:11434",
    Model = "qwen2.5vl:3b",
    FallbackOllamaBaseUrl = "http://unraid:11434",
    FallbackModel = "minicpm-v:latest"
});

AssertEqual(1, unraidPlan.Length, "Unraid MiniCPM preference should not include fallback endpoint");
AssertEqual("http://unraid:11434", unraidPlan[0].OllamaBaseUrl, "Unraid endpoint URL should come from fallback URL setting");
AssertEqual("minicpm-v:latest", unraidPlan[0].Model, "Unraid endpoint model should be MiniCPM-V");
Assert(!unraidPlan[0].IsFallback, "Unraid-only endpoint should not be marked as fallback");

var noWritableOutputsPlan = PhotoAiScanner.PlanSidecarWrites(
    new PhotoAiScanOptions
    {
        WriteJson = true,
        WriteXmp = true,
        OverwriteJson = false,
        OverwriteXmp = false
    },
    jsonExists: true,
    xmpExists: true);

AssertEqual(false, noWritableOutputsPlan.CanWriteJson, "Existing JSON should not be writable when sidecar overwrite is off");
AssertEqual(false, noWritableOutputsPlan.CanWriteXmp, "Existing XMP should not be writable when sidecar overwrite is off");
AssertEqual(false, noWritableOutputsPlan.ShouldAnalyze, "Scanner should not call the model when neither JSON nor XMP can be written");
AssertEqual(true, noWritableOutputsPlan.ShouldSkipWithoutAnalysis, "Scanner should skip before LLM when all requested sidecars already exist and overwrite is off");

var legacyJsonOverwritePlan = PhotoAiScanner.PlanSidecarWrites(
    new PhotoAiScanOptions
    {
        WriteJson = true,
        WriteXmp = true,
        OverwriteJson = true,
        OverwriteXmp = false
    },
    jsonExists: true,
    xmpExists: true);

AssertEqual(true, legacyJsonOverwritePlan.EffectiveOverwriteSidecars, "Legacy OverwriteJson should enable bundled sidecar overwrite");
AssertEqual(true, legacyJsonOverwritePlan.CanWriteJson, "Bundled overwrite should make JSON writable");
AssertEqual(true, legacyJsonOverwritePlan.CanWriteXmp, "Bundled overwrite should make XMP writable too, preventing fresh XMP with stale JSON");
AssertEqual(true, legacyJsonOverwritePlan.ShouldAnalyze, "Scanner should analyze when bundled sidecar overwrite is enabled");

var legacyXmpOverwritePlan = PhotoAiScanner.PlanSidecarWrites(
    new PhotoAiScanOptions
    {
        WriteJson = true,
        WriteXmp = true,
        OverwriteJson = false,
        OverwriteXmp = true
    },
    jsonExists: true,
    xmpExists: true);

AssertEqual(true, legacyXmpOverwritePlan.EffectiveOverwriteSidecars, "Legacy OverwriteXmp should enable bundled sidecar overwrite");
AssertEqual(true, legacyXmpOverwritePlan.CanWriteJson, "Bundled overwrite should make JSON writable when XMP overwrite was requested");
AssertEqual(true, legacyXmpOverwritePlan.CanWriteXmp, "Bundled overwrite should make XMP writable");

var estimatingSnapshot = PhotoAiRunProgressSnapshot.Create(
    state: PhotoAiRunState.Running,
    phase: "Generating descriptions",
    startedAt: new DateTimeOffset(2026, 5, 22, 21, 0, 0, TimeSpan.Zero),
    now: new DateTimeOffset(2026, 5, 22, 21, 5, 0, TimeSpan.Zero),
    totalFiles: 100,
    completedFiles: 9,
    skippedFiles: 0,
    failedFiles: 0);

AssertEqual(9, estimatingSnapshot.FilesFinished, "Finished files should include completed, skipped, and failed counts");
AssertEqual(null, estimatingSnapshot.EstimatedRemaining, "ETA should stay unavailable until at least 10 files are completed");
AssertEqual(null, estimatingSnapshot.EstimatedFinishTime, "Estimated finish time should stay unavailable until at least 10 files are completed");
AssertEqual("Estimating...", estimatingSnapshot.EstimatedRemainingDisplay, "Remaining display should use Estimating before the threshold");
AssertEqual("Estimating...", estimatingSnapshot.EstimatedFinishTimeDisplay, "ETA display should use Estimating before the threshold");
AssertEqual(9.0, estimatingSnapshot.ProgressFraction, "Progress should still report raw finished/total percentage before ETA threshold");

var estimatedSnapshot = PhotoAiRunProgressSnapshot.Create(
    state: PhotoAiRunState.Running,
    phase: "Generating descriptions",
    startedAt: new DateTimeOffset(2026, 5, 22, 21, 0, 0, TimeSpan.Zero),
    now: new DateTimeOffset(2026, 5, 22, 21, 10, 0, TimeSpan.Zero),
    totalFiles: 100,
    completedFiles: 10,
    skippedFiles: 5,
    failedFiles: 0);

AssertEqual(15, estimatedSnapshot.FilesFinished, "Finished files should include skipped files for progress");
AssertEqual(15.0, estimatedSnapshot.ProgressFraction, "Progress should be based on all finished files over total files");
AssertEqual(TimeSpan.FromMinutes(85), estimatedSnapshot.EstimatedRemaining, "Remaining estimate should use completed work after the threshold");
AssertEqual(new DateTimeOffset(2026, 5, 22, 22, 35, 0, TimeSpan.Zero), estimatedSnapshot.EstimatedFinishTime, "ETA should be now plus estimated remaining time");
AssertEqual("01:25:00", estimatedSnapshot.EstimatedRemainingDisplay, "Remaining display should format hour-long estimates as hh:mm:ss");
AssertEqual("2026-05-22 22:35", estimatedSnapshot.EstimatedFinishTimeDisplay, "ETA display should format the estimated finish time");

var scanSnapshot = PhotoAiRunProgressSnapshot.Create(
    state: PhotoAiRunState.Scanning,
    phase: "Scanning folder",
    startedAt: new DateTimeOffset(2026, 5, 22, 21, 0, 0, TimeSpan.Zero),
    now: new DateTimeOffset(2026, 5, 22, 21, 1, 0, TimeSpan.Zero),
    totalFiles: null,
    completedFiles: 0,
    skippedFiles: 0,
    failedFiles: 0);

AssertEqual(true, scanSnapshot.IsIndeterminate, "Progress should be indeterminate while scanning before total files are known");
AssertEqual(null, scanSnapshot.ProgressFraction, "Indeterminate progress should not expose a percentage");

var completedSummaryDocument = PhotoAiRunSummaryDocument.FromSummary(new PhotoAiScanSummary
{
    RootPath = "/photos/library",
    RunLogPath = "/photos/library/.photoai/photoai-run-20260522-210000.log",
    StartTime = new DateTimeOffset(2026, 5, 22, 21, 0, 0, TimeSpan.Zero),
    StopTime = new DateTimeOffset(2026, 5, 22, 21, 12, 0, TimeSpan.Zero),
    ImagesFound = 12,
    Completed = 10,
    Skipped = 1,
    Failed = 1,
    ParseFailed = 2,
    XmpWritten = 8,
    JsonWriteSkipped = 3,
    XmpWriteSkipped = 4,
    ModelFailures = 1,
    FallbackAttempts = 1,
    FallbackSucceeded = 1,
    DryRun = false
});

AssertEqual(PhotoAiRunState.Completed, completedSummaryDocument.State, "Completed summary document should expose completed state");
Assert(completedSummaryDocument.Title.Contains("PhotoAI Run Summary", StringComparison.Ordinal), "Summary document should have a user-facing title");
Assert(completedSummaryDocument.PlainText.Contains("Files processed: 10", StringComparison.Ordinal), "Summary document should include processed file count");
Assert(completedSummaryDocument.PlainText.Contains("XMP files successfully written: 8", StringComparison.Ordinal), "Summary document should include XMP write count");
Assert(completedSummaryDocument.PlainText.Contains("Anomaly log: /photos/library/.photoai/photoai-run-20260522-210000.log", StringComparison.Ordinal), "Summary document should include anomaly log path");
Assert(completedSummaryDocument.SuggestedFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase), "Summary document should suggest a text filename");
Assert(!completedSummaryDocument.SuggestedFileName.Any(Path.GetInvalidFileNameChars().Contains), "Suggested summary filename should be safe for Save As dialogs");
Assert(completedSummaryDocument.Json.Contains("\"completed\": 10", StringComparison.Ordinal), "Summary document JSON should include completed count");
Assert(completedSummaryDocument.Json.Contains("\"run_log_path\": \"/photos/library/.photoai/photoai-run-20260522-210000.log\"", StringComparison.Ordinal), "Summary document JSON should include run log path");

var dryRunSummaryDocument = PhotoAiRunSummaryDocument.FromSummary(new PhotoAiScanSummary
{
    RootPath = "/photos/library",
    RunLogPath = "(dry run - no anomaly log written)",
    StartTime = new DateTimeOffset(2026, 5, 22, 21, 0, 0, TimeSpan.Zero),
    StopTime = new DateTimeOffset(2026, 5, 22, 21, 2, 0, TimeSpan.Zero),
    ImagesFound = 5,
    WouldProcess = 3,
    WouldWriteJsonSidecar = 2,
    WouldWriteXmpSidecar = 2,
    WouldSkipJsonSidecar = 1,
    WouldSkipXmpSidecar = 1,
    ExistingJsonSidecars = 2,
    ExistingXmpSidecars = 2,
    Failed = 1,
    DryRun = true
});

AssertEqual(PhotoAiRunState.Completed, dryRunSummaryDocument.State, "Dry-run summary document should expose completed state");
Assert(dryRunSummaryDocument.PlainText.Contains("Mode: Dry run", StringComparison.Ordinal), "Dry-run summary should identify preview mode");
Assert(dryRunSummaryDocument.PlainText.Contains("Would process: 3", StringComparison.Ordinal), "Dry-run summary should include would-process count");
Assert(dryRunSummaryDocument.PlainText.Contains("Would write XMP: 2", StringComparison.Ordinal), "Dry-run summary should include would-write XMP count");

string repositoryRoot = FindRepositoryRoot();
string mainFormSource = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "PhotoAIApp.Gui", "MainForm.cs"));
Assert(mainFormSource.Contains("FormBorderStyle = FormBorderStyle.Sizable", StringComparison.Ordinal),
    "Main GUI window should explicitly be sizeable so users can expand clipped sections");
Assert(!mainFormSource.Contains("panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));", StringComparison.Ordinal),
    "Live status panel should not use fixed 32px rows that clip on scaled displays");
Assert(!mainFormSource.Contains("container.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));", StringComparison.Ordinal),
    "Live status labels should not use fixed 14px rows that clip on scaled displays");
Assert(mainFormSource.Contains("ColumnCount = 3", StringComparison.Ordinal),
    "Live status should use a less crowded 3-column layout instead of six status fields in one row");
Assert(!mainFormSource.Contains("Recent status is limited to the last 5 lines below", StringComparison.Ordinal),
    "Live status should not spend vertical space on a clipped explanatory hint line");
Assert(!mainFormSource.Contains("CreateStatusValueLabel(\"Estimating...\")", StringComparison.Ordinal),
    "Idle live-status fields should initialize blank instead of showing Estimating before a run starts");
Assert(mainFormSource.Contains("ClearLiveStatus();", StringComparison.Ordinal),
    "GUI should clear live-status fields when no scan is running");
Assert(mainFormSource.Contains("Height = 40", StringComparison.Ordinal),
    "Action buttons should be tall enough to avoid vertical clipping on scaled displays");
Assert(!mainFormSource.Contains("Height = 34", StringComparison.Ordinal),
    "Browse, refresh, and dialog buttons should not use cramped 34px heights on scaled displays");
Assert(mainFormSource.Contains("Color.FromArgb(46, 125, 50)", StringComparison.Ordinal),
    "Run scan button should use a green background");
Assert(mainFormSource.Contains("Color.FromArgb(249, 168, 37)", StringComparison.Ordinal),
    "Pause button should use a yellow background");
Assert(mainFormSource.Contains("Color.FromArgb(198, 40, 40)", StringComparison.Ordinal),
    "Stop button should use a red background");
Assert(mainFormSource.Contains("panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));", StringComparison.Ordinal),
    "Scan-target rows should be tall enough for 40px buttons plus vertical margins");
Assert(!mainFormSource.Contains("button.Margin = new Padding(0, 0, 0, 8);", StringComparison.Ordinal),
    "Scan-target row buttons should not lose 8px of vertical space to bottom margin");

string tempRoot = Path.Combine(Path.GetTempPath(), $"photoai-foundation-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);
try
{
    await File.WriteAllTextAsync(Path.Combine(tempRoot, "one.jpg"), "fake image content");
    await File.WriteAllTextAsync(Path.Combine(tempRoot, "two.png"), "fake image content");

    var progressItems = new List<PhotoAiScanProgress>();
    var dryRunSummary = await new PhotoAiScanner().ScanFolderAsync(
        new PhotoAiScanOptions
        {
            FolderPath = tempRoot,
            SafetyRootPath = tempRoot,
            Recursive = false,
            DryRun = true,
            Force = false,
            WriteJson = true,
            WriteXmp = true
        },
        new CollectingProgress<PhotoAiScanProgress>(progressItems));

    AssertEqual(2, dryRunSummary.ImagesFound, "Dry run should scan candidate image count");
    Assert(progressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.Scanning, IsIndeterminate: true }),
        "Scanner should emit an indeterminate scanning snapshot before total files are known");
    Assert(progressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.DryRunning, TotalFiles: 2 }),
        "Scanner should emit dry-run snapshots after the total file count is known");
    Assert(progressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.DryRunning, FilesFinished: 1, TotalFiles: 2 }),
        "Scanner should emit per-file dry-run progress snapshots as planned actions are counted");
    AssertEqual(PhotoAiRunState.Completed, progressItems.Last(item => item.Snapshot is not null).Snapshot!.State,
        "Last structured dry-run progress snapshot should mark the run completed");
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
}

string realRunRoot = Path.Combine(Path.GetTempPath(), $"photoai-realrun-progress-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(realRunRoot);
try
{
    string imagePath = Path.Combine(realRunRoot, "already-done.jpg");
    await File.WriteAllTextAsync(imagePath, "fake image content");
    await File.WriteAllTextAsync(PhotoAiScanner.GetPhotoAiSidecarPath(imagePath), "{}");

    var progressItems = new List<PhotoAiScanProgress>();
    var summary = await new PhotoAiScanner().ScanFolderAsync(
        new PhotoAiScanOptions
        {
            FolderPath = realRunRoot,
            SafetyRootPath = realRunRoot,
            Recursive = false,
            DryRun = false,
            Force = false,
            WriteJson = true,
            WriteXmp = true
        },
        new CollectingProgress<PhotoAiScanProgress>(progressItems));

    AssertEqual(1, summary.Skipped, "Real run should skip an image with an existing PhotoAI sidecar when Force is off");
    Assert(progressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.Running, FilesFinished: 1, SkippedFiles: 1, TotalFiles: 1 }),
        "Real run should emit a running snapshot when an image is skipped");
    AssertEqual(PhotoAiRunState.Completed, progressItems.Last(item => item.Snapshot is not null).Snapshot!.State,
        "Last structured real-run progress snapshot should mark the run completed");
}
finally
{
    Directory.Delete(realRunRoot, recursive: true);
}

string pausedRunRoot = Path.Combine(Path.GetTempPath(), $"photoai-paused-progress-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(pausedRunRoot);
try
{
    string imagePath = Path.Combine(pausedRunRoot, "already-done.jpg");
    await File.WriteAllTextAsync(imagePath, "fake image content");
    await File.WriteAllTextAsync(PhotoAiScanner.GetPhotoAiSidecarPath(imagePath), "{}");

    var pauseController = new PhotoAiPauseController();
    pauseController.Pause();
    var progressItems = new List<PhotoAiScanProgress>();
    Task<PhotoAiScanSummary> scanTask = new PhotoAiScanner().ScanFolderAsync(
        new PhotoAiScanOptions
        {
            FolderPath = pausedRunRoot,
            SafetyRootPath = pausedRunRoot,
            Recursive = false,
            DryRun = false,
            Force = false,
            WriteJson = true,
            WriteXmp = true,
            PauseController = pauseController
        },
        new CollectingProgress<PhotoAiScanProgress>(progressItems));

    await WaitUntilAsync(() => progressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.Paused }), TimeSpan.FromSeconds(5));
    pauseController.Resume();
    await scanTask;

    Assert(progressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.Paused }),
        "Paused run should emit a paused structured snapshot");
    Assert(progressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.Running }),
        "Paused run should emit a running structured snapshot after resume");
}
finally
{
    Directory.Delete(pausedRunRoot, recursive: true);
}

string cancelRunRoot = Path.Combine(Path.GetTempPath(), $"photoai-cancel-progress-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(cancelRunRoot);
try
{
    await File.WriteAllTextAsync(Path.Combine(cancelRunRoot, "cancel-me.jpg"), "fake image content");

    using var cts = new CancellationTokenSource();
    var pauseController = new PhotoAiPauseController();
    pauseController.Pause();
    var progressItems = new List<PhotoAiScanProgress>();

    bool cancelled = false;
    try
    {
        Task<PhotoAiScanSummary> scanTask = new PhotoAiScanner().ScanFolderAsync(
            new PhotoAiScanOptions
            {
                FolderPath = cancelRunRoot,
                SafetyRootPath = cancelRunRoot,
                Recursive = false,
                DryRun = false,
                Force = true,
                WriteJson = false,
                WriteXmp = false,
                PauseController = pauseController
            },
            new CollectingProgress<PhotoAiScanProgress>(progressItems),
            cts.Token);
        await WaitUntilAsync(() => progressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.Paused }), TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        await scanTask;
    }
    catch (OperationCanceledException)
    {
        cancelled = true;
    }

    Assert(cancelled, "Cancelled run should throw OperationCanceledException");
    Assert(progressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.Cancelled }),
        "Cancelled run should emit a cancelled structured snapshot");
}
finally
{
    Directory.Delete(cancelRunRoot, recursive: true);
}

Console.WriteLine("PhotoAIApp.Tests passed.");

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    DateTimeOffset deadline = DateTimeOffset.Now + timeout;
    while (!condition())
    {
        if (DateTimeOffset.Now >= deadline)
        {
            throw new TimeoutException("Condition was not met before timeout.");
        }

        await Task.Delay(25);
    }
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "PhotoAIApp.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate PhotoAIApp repository root from test output directory.");
}

sealed class CollectingProgress<T>(List<T> items) : IProgress<T>
{
    public void Report(T value) => items.Add(value);
}
