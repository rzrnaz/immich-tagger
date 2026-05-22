namespace PhotoAIApp.Core;

public sealed class PhotoAiScanOptions
{
    public string FolderPath { get; init; } = "";
    public string? SafetyRootPath { get; init; }
    public bool Recursive { get; init; }
    public bool Force { get; init; }
    public bool WriteJson { get; init; } = true;
    public bool WriteXmp { get; init; } = true;
    public bool OverwriteJson { get; init; }
    public bool OverwriteXmp { get; init; }
    public bool AddTags { get; init; }
    public bool DryRun { get; init; }
    public string OllamaBaseUrl { get; init; } = PhotoAiDefaults.QwenPcOllamaBaseUrl;
    public string Model { get; init; } = PhotoAiDefaults.QwenPcModel;
    public PhotoAiModelPreference ModelPreference { get; init; } = PhotoAiModelPreference.QwenPcWithUnraidFallback;
    public string FallbackOllamaBaseUrl { get; init; } = PhotoAiDefaults.UnraidOllamaBaseUrl;
    public string FallbackModel { get; init; } = PhotoAiDefaults.UnraidModel;
    public PhotoAiPauseController? PauseController { get; init; }
    public int? Limit { get; init; }
}

public enum PhotoAiModelPreference
{
    QwenPcWithUnraidFallback,
    UnraidMiniCpmOnly
}

public sealed record PhotoAiModelEndpoint(string OllamaBaseUrl, string Model, bool IsFallback);

public sealed class PhotoAiPauseController
{
    private readonly object _gate = new();
    private TaskCompletionSource _resumeSignal = CreateResumedSignal();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        lock (_gate)
        {
            if (IsPaused)
            {
                return;
            }

            IsPaused = true;
            _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource? signalToRelease = null;
        lock (_gate)
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            signalToRelease = _resumeSignal;
        }

        signalToRelease.TrySetResult();
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken = default)
    {
        Task waitTask;
        lock (_gate)
        {
            waitTask = IsPaused ? _resumeSignal.Task : Task.CompletedTask;
        }

        return waitTask.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource CreateResumedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }
}

public static class PhotoAiDefaults
{
    public const string QwenPcOllamaBaseUrl = "http://localhost:11434";
    public const string QwenPcModel = "qwen2.5vl:3b";
    public const string UnraidOllamaBaseUrl = "http://192.168.1.8:11434";
    public const string UnraidModel = "minicpm-v:latest";
    public const string OllamaBaseUrl = QwenPcOllamaBaseUrl;
    public const string Model = QwenPcModel;

    public static readonly string[] SupportedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".bmp"
    ];
}

public sealed class PhotoAiScanProgress
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string EventName { get; init; } = "";
    public string Message { get; init; } = "";
    public string? ImagePath { get; init; }
}

public sealed class PhotoAiScanSummary
{
    public string RootPath { get; init; } = "";
    public string RunLogPath { get; init; } = "";
    public int ImagesFound { get; set; }
    public int Completed { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int ParseFailed { get; set; }
    public int XmpWritten { get; set; }
    public bool DryRun { get; set; }
    public int WouldProcess { get; set; }
    public int WouldWriteJsonSidecar { get; set; }
    public int WouldWriteXmpSidecar { get; set; }
    public int WouldSkipJsonSidecar { get; set; }
    public int WouldSkipXmpSidecar { get; set; }
    public int ExistingJsonSidecars { get; set; }
    public int ExistingXmpSidecars { get; set; }
    public int JsonWriteSkipped { get; set; }
    public int XmpWriteSkipped { get; set; }
}
