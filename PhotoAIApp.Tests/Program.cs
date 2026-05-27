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

string multiFolderRoot = Path.Combine(Path.GetTempPath(), $"photoai-multifolder-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(multiFolderRoot);
try
{
    string folderA = Path.Combine(multiFolderRoot, "2020");
    string folderB = Path.Combine(multiFolderRoot, "2021");
    Directory.CreateDirectory(folderA);
    Directory.CreateDirectory(folderB);

    string[] normalizedFolders = PhotoAiFolderSelection.NormalizeAndValidateSelectedFolders(
    [
        folderA,
        folderB,
        folderA + Path.DirectorySeparatorChar
    ], multiFolderRoot);

    AssertEqual(2, normalizedFolders.Length, "Multi-folder selection should de-duplicate selected folders");
    AssertEqual(Path.GetFullPath(folderA), normalizedFolders[0], "Multi-folder selection should preserve folder order");
    AssertEqual(Path.GetFullPath(folderB), normalizedFolders[1], "Multi-folder selection should include second selected folder");

    string[] manyFolders = Enumerable.Range(0, 6)
        .Select(index => Path.Combine(multiFolderRoot, $"folder-{index}"))
        .ToArray();
    foreach (string folder in manyFolders)
    {
        Directory.CreateDirectory(folder);
    }

    string[] normalizedManyFolders = PhotoAiFolderSelection.NormalizeAndValidateSelectedFolders(manyFolders, multiFolderRoot);
    AssertEqual(6, normalizedManyFolders.Length, "Selected folder tree should support every available folder, not an artificial 1-5 cap");

    string[] normalizedWithoutSafetyRoot = PhotoAiFolderSelection.NormalizeAndValidateSelectedFolders([folderA]);
    AssertEqual(1, normalizedWithoutSafetyRoot.Length, "GUI folder selection should no longer require a separate Immich library root/safety root");
}
finally
{
    Directory.Delete(multiFolderRoot, recursive: true);
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
AssertEqual(1440, qwenPlan[0].MaxImageDimensionPixels, "Primary endpoint should use primary Max Image Size");
AssertEqual(0, qwenPlan[1].MaxImageDimensionPixels, "Fallback endpoint should use fallback Max Image Size");

AssertEqual(1440, PhotoAiDefaults.QwenMaxImageDimensionPixels, "Qwen image resize default should match current test max dimension");
AssertEqual(1440, new PhotoAiScanOptions().MaxImageDimensionPixels, "Scan options should default to the current Qwen resize dimension");
AssertEqual(null, PhotoAiDefaults.OllamaKeepAlive, "Ollama keep_alive should be omitted so Ollama uses its configured/default retention, currently 5 minutes by default");
AssertEqual("0s", PhotoAiDefaults.OllamaUnloadKeepAlive, "Ollama unload keep_alive should explicitly unload models after the scan");
AssertEqual(3, PhotoAiDefaults.OllamaAnalysisTimeoutMinutes, "Primary and fallback Ollama image analysis calls should use the split-the-difference 3-minute timeout");
AssertEqual(1000, PhotoAiDefaults.PrimaryTransientRetryDelayMilliseconds, "Primary transient retries should pause briefly before trying Qwen again");
AssertEqual("qwen2.5vl:7b", PhotoAiDefaults.QwenPcModel, "Qwen PC default should now be the validated 7B model");

var highQualityProfile = PhotoAiModelProfile.GetPreset(PhotoAiModelProfileId.HighQuality);
AssertEqual(PhotoAiModelProfileId.HighQuality, highQualityProfile.ProfileId, "High Quality preset should identify itself");
AssertEqual("High Quality - Qwen 7B full-res", highQualityProfile.DisplayName, "High Quality preset label should be user-friendly");
Assert(highQualityProfile.OllamaBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && highQualityProfile.OllamaBaseUrl.EndsWith(":11434", StringComparison.OrdinalIgnoreCase),
    "High Quality preset should target the detected Windows host IP on Ollama port 11434");
AssertEqual("qwen2.5vl:7b", highQualityProfile.Model, "High Quality preset should use validated Qwen 7B");
AssertEqual(0, highQualityProfile.MaxImageDimensionPixels, "High Quality preset should send original/full-resolution images");
AssertEqual(0, highQualityProfile.FallbackMaxImageDimensionPixels, "High Quality fallback should default to original/full-resolution unless changed in Advanced settings");
AssertEqual(PhotoAiModelPreference.QwenPcWithUnraidFallback, highQualityProfile.ModelPreference, "High Quality should keep Unraid fallback enabled");
Assert(highQualityProfile.Summary.Contains("Original/full-res", StringComparison.Ordinal), "High Quality summary should make original/full-res obvious");

var balancedProfile = PhotoAiModelProfile.GetPreset(PhotoAiModelProfileId.Balanced);
AssertEqual("qwen2.5vl:7b", balancedProfile.Model, "Balanced preset should also use Qwen 7B");
AssertEqual(1440, balancedProfile.MaxImageDimensionPixels, "Balanced preset should cap images at 1440px");
Assert(balancedProfile.Summary.Contains("1440", StringComparison.Ordinal), "Balanced summary should include max image edge");

var compatibilityProfile = PhotoAiModelProfile.GetPreset(PhotoAiModelProfileId.Compatibility);
AssertEqual(PhotoAiModelPreference.UnraidMiniCpmOnly, compatibilityProfile.ModelPreference, "Compatibility preset should target Unraid MiniCPM directly");
AssertEqual("http://192.168.1.8:11434", compatibilityProfile.OllamaBaseUrl, "Compatibility preset should target Unraid Ollama");
AssertEqual("minicpm-v:latest", compatibilityProfile.Model, "Compatibility preset should use MiniCPM-V");
AssertEqual(0, compatibilityProfile.MaxImageDimensionPixels, "Compatibility preset should use full/original resolution after successful MiniCPM-V full-size validation");
AssertEqual(0, compatibilityProfile.FallbackMaxImageDimensionPixels, "Compatibility preset should keep fallback max image size at full/original resolution");

var customProfile = highQualityProfile with { OllamaBaseUrl = "http://10.0.0.5:11435", Model = "llava:latest", MaxImageDimensionPixels = 2222 };
AssertEqual(PhotoAiModelProfileId.Custom, PhotoAiModelProfile.MatchPreset(customProfile), "Changed endpoint/model/image edge should become Custom");
AssertEqual(PhotoAiModelProfileId.HighQuality, PhotoAiModelProfile.MatchPreset(highQualityProfile), "Exact High Quality settings should match the High Quality preset");
AssertEqual("http://10.0.0.5:11435", PhotoAiModelProfile.BuildBaseUrl("10.0.0.5", 11435), "Host/port helper should build Ollama base URLs");
AssertEqual("http://localhost:11434", PhotoAiModelProfile.BuildBaseUrl("http://localhost:11434", 11434), "URL helper should preserve explicit URL input");

var persistedSettings = new PhotoAiGuiSettings
{
    SelectedProfileId = PhotoAiModelProfileId.Custom,
    CustomProfile = customProfile
};
string persistedJson = System.Text.Json.JsonSerializer.Serialize(persistedSettings);
PhotoAiGuiSettings? restoredSettings = System.Text.Json.JsonSerializer.Deserialize<PhotoAiGuiSettings>(persistedJson);
Assert(restoredSettings is not null, "GUI settings should deserialize");
AssertEqual(PhotoAiModelProfileId.Custom, restoredSettings!.SelectedProfileId, "Selected profile should round-trip through JSON");
AssertEqual("llava:latest", restoredSettings.EffectiveProfile.Model, "Custom model should round-trip through JSON");
AssertEqual(2222, restoredSettings.EffectiveProfile.MaxImageDimensionPixels, "Custom image edge should round-trip through JSON");
AssertEqual(0, restoredSettings.EffectiveProfile.FallbackMaxImageDimensionPixels, "Custom fallback image edge should round-trip through JSON/defaults");

var ollamaRequestJson = System.Text.Json.JsonSerializer.Serialize(new OllamaGenerateRequest
{
    Model = "qwen2.5vl:3b",
    Prompt = "test",
    Images = ["abc"],
    Stream = false,
    Format = "json",
    KeepAlive = PhotoAiDefaults.OllamaKeepAlive
});
Assert(!ollamaRequestJson.Contains("\"keep_alive\"", StringComparison.Ordinal), "Ollama request JSON should omit keep_alive during the scan so Ollama uses its default retention");

var wideResize = PhotoAiScanner.CalculateResizeDimensions(4284, 2000, 1080);
AssertEqual(1080, wideResize.Width, "Wide image resize should cap the longest edge at max dimension");
AssertEqual(504, wideResize.Height, "Wide image resize should preserve aspect ratio with nearest integer rounding");
AssertEqual(true, wideResize.WasResized, "Wide oversized image should be marked as resized");

var smallResize = PhotoAiScanner.CalculateResizeDimensions(1200, 800, 1600);
AssertEqual(1200, smallResize.Width, "Small image should keep original width");
AssertEqual(800, smallResize.Height, "Small image should keep original height");
AssertEqual(false, smallResize.WasResized, "Small image should not be marked as resized");

var disabledResize = PhotoAiScanner.CalculateResizeDimensions(4284, 2000, 0);
AssertEqual(4284, disabledResize.Width, "Max dimension 0 should disable resizing and keep original width");
AssertEqual(2000, disabledResize.Height, "Max dimension 0 should disable resizing and keep original height");
AssertEqual(false, disabledResize.WasResized, "Disabled resizing should not be marked as resized");

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
AssertEqual(0, unraidPlan[0].MaxImageDimensionPixels, "Unraid-only endpoint should use fallback Max Image Size");

var primaryOnlyPlan = PhotoAiScanner.BuildModelEndpointPlan(new PhotoAiScanOptions
{
    ModelPreference = PhotoAiModelPreference.PrimaryOnly,
    OllamaBaseUrl = "http://custom:11434",
    Model = "llava:latest",
    FallbackOllamaBaseUrl = "http://unraid:11434",
    FallbackModel = "minicpm-v:latest"
});
AssertEqual(1, primaryOnlyPlan.Length, "Primary-only preference should not add a fallback endpoint");
AssertEqual("http://custom:11434", primaryOnlyPlan[0].OllamaBaseUrl, "Primary-only endpoint should use the configured primary URL");
AssertEqual("llava:latest", primaryOnlyPlan[0].Model, "Primary-only endpoint should use the configured primary model");
AssertEqual(false, primaryOnlyPlan[0].IsFallback, "Primary-only endpoint should not be marked as fallback");

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
AssertEqual(0, estimatingSnapshot.PrimaryRetryAttempts, "Primary retry count should default to zero for older snapshot call sites");
AssertEqual(0, estimatingSnapshot.FallbackAttempts, "Fallback count should default to zero for older snapshot call sites");
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
    failedFiles: 0,
    primaryRetryAttempts: 2,
    fallbackAttempts: 1);

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
    PrimaryRetryAttempts = 1,
    PrimaryRetrySucceeded = 1,
    PrimaryRetryFailed = 0,
    FallbackAttempts = 1,
    FallbackSucceeded = 1,
    DryRun = false
});

AssertEqual(PhotoAiRunState.Completed, completedSummaryDocument.State, "Completed summary document should expose completed state");
Assert(completedSummaryDocument.Title.Contains("PhotoAI Run Summary", StringComparison.Ordinal), "Summary document should have a user-facing title");
Assert(completedSummaryDocument.PlainText.Contains("Average/photo: 72.0 seconds", StringComparison.Ordinal), "Summary document should show average time per photo with one decimal place in seconds");
Assert(completedSummaryDocument.PlainText.Contains("Files processed: 10", StringComparison.Ordinal), "Summary document should include processed file count");
Assert(completedSummaryDocument.PlainText.Contains("XMP files successfully written: 8", StringComparison.Ordinal), "Summary document should include XMP write count");
Assert(completedSummaryDocument.PlainText.Contains("Primary retry attempts: 1", StringComparison.Ordinal), "Summary document should include primary retry attempt count");
Assert(completedSummaryDocument.PlainText.Contains("Primary retry successes: 1", StringComparison.Ordinal), "Summary document should include primary retry success count");
Assert(completedSummaryDocument.PlainText.Contains("Primary retry failures: 0", StringComparison.Ordinal), "Summary document should include primary retry failure count");
Assert(completedSummaryDocument.PlainText.Contains("Anomaly log: /photos/library/.photoai/photoai-run-20260522-210000.log", StringComparison.Ordinal), "Summary document should include anomaly log path");
Assert(completedSummaryDocument.SuggestedFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase), "Summary document should suggest a text filename");
Assert(!completedSummaryDocument.SuggestedFileName.Any(Path.GetInvalidFileNameChars().Contains), "Suggested summary filename should be safe for Save As dialogs");
Assert(completedSummaryDocument.Json.Contains("\"completed\": 10", StringComparison.Ordinal), "Summary document JSON should include completed count");
Assert(completedSummaryDocument.Json.Contains("\"primary_retry_attempts\": 1", StringComparison.Ordinal), "Summary document JSON should include primary retry attempt count");
Assert(completedSummaryDocument.Json.Contains("\"primary_retry_successes\": 1", StringComparison.Ordinal), "Summary document JSON should include primary retry success count");
Assert(completedSummaryDocument.Json.Contains("\"primary_retry_failures\": 0", StringComparison.Ordinal), "Summary document JSON should include primary retry failure count");
Assert(completedSummaryDocument.Json.Contains("\"run_log_path\": \"/photos/library/.photoai/photoai-run-20260522-210000.log\"", StringComparison.Ordinal), "Summary document JSON should include run log path");

var dryRunSummaryDocument = PhotoAiRunSummaryDocument.FromSummary(new PhotoAiScanSummary
{
    RootPath = "/photos/library",
    RunLogPath = "(dry run - no anomaly log written)",
    StartTime = new DateTimeOffset(2026, 5, 22, 21, 0, 0, TimeSpan.Zero),
    StopTime = new DateTimeOffset(2026, 5, 22, 21, 2, 0, TimeSpan.Zero),
    ImagesFound = 19,
    Skipped = 7,
    WouldProcess = 12,
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
Assert(dryRunSummaryDocument.PlainText.Contains("Would skip files: 7", StringComparison.Ordinal), "Dry-run summary should include file-level skips separately from sidecar-write skips");
Assert(dryRunSummaryDocument.PlainText.Contains("Would process: 12", StringComparison.Ordinal), "Dry-run summary should include would-process count");
Assert(dryRunSummaryDocument.PlainText.Contains("Estimated time to process: 0 hours 1 minute", StringComparison.Ordinal), "Dry-run summary should estimate processing time from files that would actually be processed, excluding skipped/not-overwritten files");
Assert(!dryRunSummaryDocument.PlainText.Contains("files x 5.6 seconds", StringComparison.Ordinal), "Dry-run text summary should not include the calculation methodology");
Assert(!dryRunSummaryDocument.PlainText.Contains("19 files x 5.6 seconds", StringComparison.Ordinal), "Dry-run estimate should not use all images found when some files would be skipped");
Assert(dryRunSummaryDocument.Json.Contains("\"estimated_processing_minutes\": 1", StringComparison.Ordinal), "Dry-run summary JSON should include estimated processing minutes");
Assert(dryRunSummaryDocument.Json.Contains("\"estimated_processing_seconds_per_file\": 5.6", StringComparison.Ordinal), "Dry-run summary JSON should record the 5.6-second-per-file estimate basis");
Assert(dryRunSummaryDocument.PlainText.Contains("Would write XMP files: 2", StringComparison.Ordinal), "Dry-run summary should include would-write XMP count");
Assert(dryRunSummaryDocument.PlainText.Contains("Would skip XMP writes: 1", StringComparison.Ordinal), "Dry-run summary should label sidecar-write skips as write skips, not file skips");

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
Assert(mainFormSource.Contains("AddStatusPair(panel, 0, 2, \"Retry\", _primaryRetryValueLabel);", StringComparison.Ordinal),
    "Live status should show primary retry attempts during long Qwen runs");
Assert(mainFormSource.Contains("AddStatusPair(panel, 1, 2, \"Fallback\", _fallbackValueLabel);", StringComparison.Ordinal),
    "Live status should show fallback attempts during long Qwen runs");
Assert(mainFormSource.Contains("Math.Round(snapshot.ProgressFraction.Value)", StringComparison.Ordinal),
    "Files count should include an integer percent complete when total progress is known");
Assert(mainFormSource.Contains("FormatSummaryTimestamp(snapshot.EstimatedFinishTime)", StringComparison.Ordinal),
    "ETA should include the same local timezone suffix as the start time");
Assert(!mainFormSource.Contains("Recent status is limited to the last 5 lines below", StringComparison.Ordinal),
    "Live status should not spend vertical space on a clipped explanatory hint line");
Assert(!mainFormSource.Contains("CreateStatusValueLabel(\"Estimating...\")", StringComparison.Ordinal),
    "Idle live-status fields should initialize blank instead of showing Estimating before a run starts");
Assert(mainFormSource.Contains("ClearLiveStatus();", StringComparison.Ordinal),
    "GUI should clear live-status fields when no scan is running");
Assert(mainFormSource.IndexOf("ClearLiveStatus();\n            ShowRunSummary", StringComparison.Ordinal) >= 0,
    "GUI should stop/clear the live progress bar before opening the modal run summary");
Assert(mainFormSource.Contains("Height = 40", StringComparison.Ordinal),
    "Action buttons should be tall enough to avoid vertical clipping on scaled displays");
Assert(!mainFormSource.Contains("Height = 34", StringComparison.Ordinal),
    "Browse, refresh, and dialog buttons should not use cramped 34px heights on scaled displays");
Assert(mainFormSource.Contains("Text = \"Run Scan\"", StringComparison.Ordinal),
    "Run Scan button label should use the requested capitalization");
Assert(mainFormSource.Contains("Text = \"Open Log\"", StringComparison.Ordinal),
    "Open Log button label should use the requested capitalization");
Assert(mainFormSource.Contains("PhotoAiTheme.WarmBlue", StringComparison.Ordinal),
    "Run Scan and Open Log buttons should use the shared warm blue background");
Assert(mainFormSource.Contains("StyleActionButton(_runButton, PhotoAiTheme.WarmBlue, PhotoAiTheme.SurfaceRaised)", StringComparison.Ordinal),
    "Run Scan button text should use the same warm beige color as Stop for readability on warm blue");
Assert(mainFormSource.Contains("StyleActionButton(_openLogButton, PhotoAiTheme.WarmBlue, PhotoAiTheme.SurfaceRaised)", StringComparison.Ordinal),
    "Open Log button text should use the same warm beige color as Stop for readability on warm blue");
Assert(mainFormSource.Contains("PhotoAiTheme.WarmYellow", StringComparison.Ordinal),
    "Pause button should use a yellow background");
Assert(mainFormSource.Contains("PhotoAiTheme.WarmRed", StringComparison.Ordinal),
    "Stop button should use a red background");
Assert(mainFormSource.Contains("panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));", StringComparison.Ordinal),
    "Scan-target rows should be tall enough for 40px buttons plus vertical margins");
Assert(!mainFormSource.Contains("button.Margin = new Padding(0, 0, 0, 8);", StringComparison.Ordinal),
    "Scan-target row buttons should not lose 8px of vertical space to bottom margin");

Assert(mainFormSource.Contains("Height = 320", StringComparison.Ordinal),
    "Running log area should be tall enough to show 10 recent lines without clipping");
Assert(mainFormSource.Contains("while (_recentStatusLines.Count > 10)", StringComparison.Ordinal),
    "Running log should retain the 10 most recent lines");
Assert(mainFormSource.Contains("ConfigureToolTips();", StringComparison.Ordinal),
    "Main form should configure hover help for important controls");
Assert(mainFormSource.Contains("Max Image Size", StringComparison.Ordinal),
    "UI should use the requested Max Image Size label");

Assert(mainFormSource.Contains("TreeView", StringComparison.Ordinal),
    "Main GUI should expose a tree-style folder browser, not a flat folder list");
Assert(!mainFormSource.Contains("Immich library root:", StringComparison.Ordinal),
    "Main GUI should not expose a separate Immich library root field");
Assert(mainFormSource.Contains("new() { Text = @\"P:\\\" }", StringComparison.Ordinal),
    "Folder source should default to P:\\ on Windows");
Assert(!mainFormSource.Contains("Load tree", StringComparison.Ordinal),
    "Folder tree should auto-load without a Load tree button");
Assert(mainFormSource.Contains("Shown += (_, _) => AutoLoadSelectableFoldersIfAvailable();", StringComparison.Ordinal),
    "Folder tree should auto-load when the form opens and the folder source exists");
Assert(!mainFormSource.Contains("Text = \"Select All\"", StringComparison.Ordinal),
    "Bulk Select All button should be removed once parent folder selection cascades to children");
Assert(!mainFormSource.Contains("Text = \"Unselect All\"", StringComparison.Ordinal),
    "Bulk Unselect All button should be removed once parent folder selection cascades to children");
Assert(mainFormSource.Contains("CheckBoxes = true", StringComparison.Ordinal),
    "Folder tree should show one checkbox per folder row for include/exclude selection");
Assert(mainFormSource.Contains("AfterCheck", StringComparison.Ordinal),
    "Folder tree should cascade parent checkbox changes to loaded children");
Assert(mainFormSource.Contains("ApplyCheckedStateToDescendants", StringComparison.Ordinal),
    "Folder tree should use a guarded checked-state cascade helper");
Assert(mainFormSource.Contains("BeforeExpand", StringComparison.Ordinal),
    "Folder tree should lazily load subfolders when a folder is expanded");
Assert(mainFormSource.Contains("LoadSubfolderNodes", StringComparison.Ordinal),
    "Folder tree should support expandable nested subfolders");
Assert(mainFormSource.Contains("GetCheckedFolderPaths", StringComparison.Ordinal),
    "Run logic should collect checked folders from the expanded tree");
Assert(mainFormSource.Contains("AppendAggregateRunCompleteAsync", StringComparison.Ordinal),
    "Multi-folder live runs should append an aggregate RUN COMPLETE block so the opened log is not last-folder-only");
Assert(mainFormSource.Contains("AGGREGATE RUN COMPLETE", StringComparison.Ordinal),
    "Aggregate log block should clearly identify the combined multi-folder completion summary");
Assert(!mainFormSource.Contains("CheckedListBox", StringComparison.Ordinal),
    "Main GUI should not use the old flat checked list for folder selection");
Assert(!mainFormSource.Contains("PhotoAiFolderSelection.MaxSelectedFolders", StringComparison.Ordinal),
    "Main GUI should not enforce an artificial 1-5 folder cap; 1-5 is only a usage pattern");
Assert(mainFormSource.Contains("UnloadModelsAtEnd = index == selectedFolders.Length - 1", StringComparison.Ordinal),
    "Multi-folder runs should keep models loaded between queued folders and unload after the last folder");

string advancedSettingsSource = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "PhotoAIApp.Gui", "AdvancedSettingsForm.cs"));
Assert(advancedSettingsSource.Contains("Test fallback", StringComparison.Ordinal),
    "Advanced dialog should expose a fallback connection test button");
Assert(advancedSettingsSource.Contains("_fallbackModelComboBox", StringComparison.Ordinal),
    "Advanced dialog should let fallback models be refreshed/selected from a dropdown");
Assert(advancedSettingsSource.Contains("_fallbackMaxImageSizeNumeric", StringComparison.Ordinal),
    "Advanced dialog should expose a fallback-specific Max Image Size control");
Assert(advancedSettingsSource.Contains("PreferredPrimaryModels", StringComparison.Ordinal),
    "Advanced dialog should offer preferred Qwen2.5-VL model tags even when /api/tags is incomplete");
Assert(!advancedSettingsSource.Contains("BlockedFallbackModelNameFragments", StringComparison.Ordinal),
    "Advanced dialog should not hide installed fallback models in code; bad models should be removed from Ollama itself");
Assert(mainFormSource.Contains("new RowStyle(SizeType.Absolute, 360)", StringComparison.Ordinal),
    "Folder tree should be tall enough to show more of the folder structure");
Assert(advancedSettingsSource.Contains("Shown += async", StringComparison.Ordinal),
    "Advanced dialog should poll Ollama model lists when opened");
Assert(advancedSettingsSource.Contains("Height = 900", StringComparison.Ordinal),
    "Advanced dialog should open tall enough to show settings and OK/Cancel buttons");
Assert(advancedSettingsSource.Contains("new ColumnStyle(SizeType.Absolute, 260)", StringComparison.Ordinal),
    "Advanced dialog label columns should be wide enough to avoid clipping labels at scaled DPI");
Assert(advancedSettingsSource.Contains("Text = \"?\"", StringComparison.Ordinal),
    "Advanced dialog should expose a visible help affordance, not only hidden hover tips");

string serverProgramSource = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "PhotoAIApp.Server", "Program.cs"));
Assert(serverProgramSource.Contains("app.MapPost(\"/api/settings\"", StringComparison.Ordinal),
    "Docker server UI should let users edit runtime settings instead of only showing template/env defaults");
Assert(serverProgramSource.Contains("app.MapGet(\"/api/folders\"", StringComparison.Ordinal),
    "Docker server UI should expose a folder browsing API for directories under /photos");
Assert(serverProgramSource.Contains("app.MapGet(\"/api/log\"", StringComparison.Ordinal),
    "Docker server UI should expose safe log links for run/anomaly logs");
Assert(serverProgramSource.Contains("validatePathForRead", StringComparison.Ordinal)
    || serverProgramSource.Contains("ValidatePathForRead", StringComparison.Ordinal),
    "Docker server log/folder endpoints should validate requested paths before reading server files");
Assert(serverProgramSource.Contains("id=\"primaryOllamaUrl\"", StringComparison.Ordinal),
    "Docker server settings editor should expose the primary Ollama URL");
Assert(serverProgramSource.Contains("id=\"primaryModel\"", StringComparison.Ordinal),
    "Docker server settings editor should expose the primary model");
Assert(serverProgramSource.Contains("id=\"fallbackEnabled\"", StringComparison.Ordinal),
    "Docker server settings editor should expose the fallback enable switch");
Assert(serverProgramSource.Contains("id=\"overwriteSidecars\"", StringComparison.Ordinal),
    "Docker server settings editor should expose the overwrite sidecars switch");
Assert(serverProgramSource.Contains("openLogLink", StringComparison.Ordinal),
    "Docker server home page should render an Open Log link when a run log path is available");
Assert(serverProgramSource.Contains("browseFolders", StringComparison.Ordinal),
    "Docker server home page should include browser JavaScript for folder selection under /photos");

string scannerSource = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "PhotoAIApp.Core", "PhotoAiScanner.cs"));
Assert(!scannerSource.Contains("Preparing image for {model}", StringComparison.Ordinal),
    "Live log should not include per-image preparation telemetry");
Assert(!scannerSource.Contains("Ollama image size unchanged", StringComparison.Ordinal),
    "Live log should not include unchanged-size image telemetry");
Assert(!scannerSource.Contains("Ollama request keep_alive=", StringComparison.Ordinal),
    "Live log should not include per-request keep_alive telemetry");
Assert(mainFormSource.Contains("!string.Equals(item.EventName, \"PROCESS\", StringComparison.OrdinalIgnoreCase)", StringComparison.Ordinal),
    "GUI running log should suppress routine PROCESS X/y rows while still applying progress snapshots");
Assert(scannerSource.Contains("Wrote {string.Join(\", \", writtenSidecarDisplayNames)}", StringComparison.Ordinal),
    "Live log should report written JSON/XMP sidecars without claiming the original JPEG was written");
Assert(!scannerSource.Contains("Wrote {fullImagePath},", StringComparison.Ordinal),
    "Live log write message should not list the original JPEG path because PhotoAIApp does not write it");
Assert(scannerSource.Contains("average_time_per_processed_photo={FormatAverageSecondsPerPhoto(summary.AverageTimePerProcessedPhoto)}", StringComparison.Ordinal),
    "Run completion log should write average photo time with one decimal place in seconds");

string tempRoot = Path.Combine(Path.GetTempPath(), $"photoai-foundation-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);
try
{
    await File.WriteAllTextAsync(Path.Combine(tempRoot, "one.jpg"), "fake image content");
    await File.WriteAllTextAsync(Path.Combine(tempRoot, "two.png"), "fake image content");
    string internalLogDirectory = Path.Combine(tempRoot, ".photoai");
    Directory.CreateDirectory(internalLogDirectory);
    await File.WriteAllTextAsync(Path.Combine(internalLogDirectory, "should-not-scan.jpg"), "fake image content");
    Assert(PhotoAiScanner.IsInExcludedScanDirectory(Path.Combine(internalLogDirectory, "should-not-scan.jpg"), tempRoot),
        "Scanner helper should identify files under internal .photoai folders as excluded");

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

    AssertEqual(2, dryRunSummary.ImagesFound, "Dry run should scan candidate image count and ignore internal .photoai folders");
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

string aggregateProgressRoot = Path.Combine(Path.GetTempPath(), $"photoai-aggregate-progress-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(aggregateProgressRoot);
try
{
    string folderA = Path.Combine(aggregateProgressRoot, "A");
    string folderB = Path.Combine(aggregateProgressRoot, "B");
    Directory.CreateDirectory(folderA);
    Directory.CreateDirectory(folderB);
    await File.WriteAllTextAsync(Path.Combine(folderA, "one.jpg"), "fake image content");
    await File.WriteAllTextAsync(Path.Combine(folderB, "two.jpg"), "fake image content");
    await File.WriteAllTextAsync(Path.Combine(folderB, "three.png"), "fake image content");

    var firstProgressItems = new List<PhotoAiScanProgress>();
    var firstSummary = await new PhotoAiScanner().ScanFolderAsync(
        new PhotoAiScanOptions
        {
            FolderPath = folderA,
            SafetyRootPath = folderA,
            Recursive = false,
            DryRun = true,
            Force = true,
            WriteJson = true,
            WriteXmp = true,
            ProgressTotalFiles = 3
        },
        new CollectingProgress<PhotoAiScanProgress>(firstProgressItems));

    var secondProgressItems = new List<PhotoAiScanProgress>();
    await new PhotoAiScanner().ScanFolderAsync(
        new PhotoAiScanOptions
        {
            FolderPath = folderB,
            SafetyRootPath = folderB,
            Recursive = false,
            DryRun = true,
            Force = true,
            WriteJson = true,
            WriteXmp = true,
            ProgressCompletedOffset = firstSummary.WouldProcess,
            ProgressTotalFiles = 3,
            ProgressStartedAt = firstSummary.StartTime
        },
        new CollectingProgress<PhotoAiScanProgress>(secondProgressItems));

    Assert(firstProgressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.DryRunning, FilesFinished: 1, TotalFiles: 3 }),
        "Aggregate progress should use the cross-folder total during the first selected folder");
    Assert(secondProgressItems.Any(item => item.Snapshot is { State: PhotoAiRunState.DryRunning, FilesFinished: 3, TotalFiles: 3 }),
        "Aggregate progress should carry the completed offset into the next selected folder");
    Assert(secondProgressItems.Any(item => item.Message.Contains("WOULD PROCESS batch 3/3, folder 2/2", StringComparison.Ordinal)),
        "Aggregate progress log messages should show batch and folder positions together");
}
finally
{
    Directory.Delete(aggregateProgressRoot, recursive: true);
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

var diagnosticSidecar = new PhotoAiSidecar
{
    SourceFile = "P:\\photoai-test\\sample.jpg",
    SourceFileName = "sample.jpg",
    SourceRoot = "P:\\photoai-test",
    RelativePath = "sample.jpg",
    OllamaBaseUrl = "http://localhost:11434",
    Model = "qwen2.5vl:3b",
    ProcessedAtUtc = DateTimeOffset.UtcNow,
    Analysis = new PhotoAnalysis { Description = "sample", ShortCaption = "sample" },
    ImageProcessing = new PhotoAiImageProcessingMetadata
    {
        OriginalWidth = 4284,
        OriginalHeight = 5712,
        SentWidth = 1080,
        SentHeight = 1440,
        WasResized = true,
        MaxImageDimensionPixels = 1440,
        OriginalBytes = 3_000_000,
        SentBytes = 500_000,
        Base64Chars = 666_668,
        ResizeElapsedMilliseconds = 42.5
    },
    OllamaStats = new PhotoAiOllamaStats
    {
        ModelReturned = "qwen2.5vl:3b",
        Done = true,
        DoneReason = "stop",
        HttpElapsedMilliseconds = 1234.5,
        TotalDurationNanoseconds = 1_200_000_000,
        LoadDurationNanoseconds = 100_000_000,
        PromptEvalCount = 512,
        EvalCount = 64,
        ResponseChars = 128
    },
    ModelAttempts =
    [
        new PhotoAiModelAttemptLog
        {
            AttemptNumber = 1,
            OllamaBaseUrl = "http://localhost:11434",
            Model = "qwen2.5vl:3b",
            IsFallback = false,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Succeeded = false,
            TimeoutSeconds = 180,
            ErrorType = "System.InvalidOperationException",
            ErrorMessage = "model runner has unexpectedly stopped"
        },
        new PhotoAiModelAttemptLog
        {
            AttemptNumber = 2,
            OllamaBaseUrl = "http://192.168.1.8:11434",
            Model = "minicpm-v:latest",
            IsFallback = true,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Succeeded = true,
            TimeoutSeconds = 180
        }
    ],
    RawResponse = "{}"
};

string compactSidecarJson = PhotoAiSidecarSerializer.Serialize(diagnosticSidecar);
Assert(compactSidecarJson.Contains("\"image_processing\"", StringComparison.Ordinal), "Sidecar JSON should include top-level image preprocessing diagnostics");
Assert(compactSidecarJson.Contains("\"ollama_stats\"", StringComparison.Ordinal), "Sidecar JSON should include top-level Ollama response diagnostics");
Assert(compactSidecarJson.Contains("\"model_attempts\"", StringComparison.Ordinal), "Sidecar JSON should include per-model attempt diagnostics");
Assert(compactSidecarJson.Contains("model runner has unexpectedly stopped", StringComparison.Ordinal), "Sidecar JSON should preserve Qwen failure messages when fallback succeeds");
Assert(!compactSidecarJson.Contains("\"base64_chars\"", StringComparison.Ordinal), "Production sidecar JSON should omit base64 character counts that were mainly useful for Qwen 3B payload debugging");
Assert(!compactSidecarJson.Contains("\"resize_elapsed_ms\"", StringComparison.Ordinal), "Production sidecar JSON should omit resize timing noise from image metadata");
AssertEqual(compactSidecarJson.IndexOf("\"image_processing\"", StringComparison.Ordinal), compactSidecarJson.LastIndexOf("\"image_processing\"", StringComparison.Ordinal), "Sidecar JSON should not duplicate image_processing inside model_attempts");
AssertEqual(compactSidecarJson.IndexOf("\"ollama_stats\"", StringComparison.Ordinal), compactSidecarJson.LastIndexOf("\"ollama_stats\"", StringComparison.Ordinal), "Sidecar JSON should not duplicate ollama_stats inside model_attempts");

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
