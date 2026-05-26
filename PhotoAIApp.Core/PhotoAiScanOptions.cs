using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

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
    public bool OverwriteSidecars => OverwriteJson || OverwriteXmp;
    public bool AddTags { get; init; }
    public bool DryRun { get; init; }
    public string OllamaBaseUrl { get; init; } = PhotoAiDefaults.QwenPcOllamaBaseUrl;
    public string Model { get; init; } = PhotoAiDefaults.QwenPcModel;
    public PhotoAiModelPreference ModelPreference { get; init; } = PhotoAiModelPreference.QwenPcWithUnraidFallback;
    public string FallbackOllamaBaseUrl { get; init; } = PhotoAiDefaults.UnraidOllamaBaseUrl;
    public string FallbackModel { get; init; } = PhotoAiDefaults.UnraidModel;
    public PhotoAiPauseController? PauseController { get; init; }
    public int MaxImageDimensionPixels { get; init; } = PhotoAiDefaults.QwenMaxImageDimensionPixels;
    public int FallbackMaxImageDimensionPixels { get; init; } = PhotoAiDefaults.UnraidMaxImageDimensionPixels;
    public int? Limit { get; init; }
    public bool UnloadModelsAtEnd { get; init; } = true;
    public int ProgressCompletedOffset { get; init; }
    public int ProgressSkippedOffset { get; init; }
    public int ProgressFailedOffset { get; init; }
    public int ProgressPrimaryRetryAttemptsOffset { get; init; }
    public int ProgressFallbackAttemptsOffset { get; init; }
    public int? ProgressTotalFiles { get; init; }
    public DateTimeOffset? ProgressStartedAt { get; init; }
}

public sealed record PhotoAiSidecarWritePlan(
    bool EffectiveOverwriteSidecars,
    bool CanWriteJson,
    bool CanWriteXmp)
{
    public bool ShouldAnalyze => CanWriteJson || CanWriteXmp;
    public bool ShouldSkipWithoutAnalysis => !ShouldAnalyze;
}

public enum PhotoAiModelPreference
{
    QwenPcWithUnraidFallback,
    UnraidMiniCpmOnly,
    PrimaryOnly
}

public sealed record PhotoAiModelEndpoint(string OllamaBaseUrl, string Model, bool IsFallback, int MaxImageDimensionPixels);

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
    public static string QwenPcOllamaBaseUrl => $"http://{DetectHostIPv4Address()}:11434";
    public const string QwenPcModel = "qwen2.5vl:7b";
    public const string UnraidOllamaBaseUrl = "http://192.168.1.8:11434";
    public const string UnraidModel = "minicpm-v:latest";
    public static string OllamaBaseUrl => QwenPcOllamaBaseUrl;
    public const string Model = QwenPcModel;
    public const int QwenMaxImageDimensionPixels = 1440;
    public const int UnraidMaxImageDimensionPixels = 0;
    public const string? OllamaKeepAlive = null;
    public const string OllamaUnloadKeepAlive = "0s";
    public const int OllamaAnalysisTimeoutMinutes = 3;
    public const int PrimaryTransientRetryDelayMilliseconds = 1000;

    public static readonly string[] SupportedExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".bmp"
    ];

    public static readonly string[] PreferredPrimaryModels =
    [
        "qwen2.5vl:7b",
        "qwen2.5vl:7b-q8_0",
        "qwen2.5vl:7b-q4_K_M",
        "qwen2.5vl:3b",
        "qwen2.5vl:3b-q8_0"
    ];


    public static string DetectHostIPv4Address()
    {
        try
        {
            string[] addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.Address)
                .Where(address => !IPAddress.IsLoopback(address))
                .Where(address => !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(address => address.ToString())
                .OrderByDescending(address => address.StartsWith("192.168.", StringComparison.Ordinal))
                .ThenByDescending(address => address.StartsWith("10.", StringComparison.Ordinal))
                .ThenByDescending(address => address.StartsWith("172.", StringComparison.Ordinal))
                .ToArray();

            return addresses.FirstOrDefault() ?? "localhost";
        }
        catch
        {
            return "localhost";
        }
    }
}

public sealed class PhotoAiScanProgress
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string EventName { get; init; } = "";
    public string Message { get; init; } = "";
    public string? ImagePath { get; init; }
    public PhotoAiRunProgressSnapshot? Snapshot { get; init; }
}

public sealed class PhotoAiScanSummary
{
    public string RootPath { get; init; } = "";
    public string RunLogPath { get; init; } = "";
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset? StopTime { get; set; }
    public TimeSpan ElapsedTime => (StopTime ?? DateTimeOffset.Now) - StartTime;
    public TimeSpan AverageTimePerProcessedPhoto => Completed > 0
        ? TimeSpan.FromTicks(ElapsedTime.Ticks / Completed)
        : TimeSpan.Zero;
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
    public int ModelFailures { get; set; }
    public int PrimaryRetryAttempts { get; set; }
    public int PrimaryRetrySucceeded { get; set; }
    public int PrimaryRetryFailed { get; set; }
    public int FallbackAttempts { get; set; }
    public int FallbackSucceeded { get; set; }

    public static PhotoAiScanSummary Combine(IReadOnlyList<PhotoAiScanSummary> summaries)
    {
        if (summaries.Count == 0)
        {
            throw new ArgumentException("At least one summary is required.", nameof(summaries));
        }

        return new PhotoAiScanSummary
        {
            RootPath = summaries.Count == 1 ? summaries[0].RootPath : $"Multiple folders ({summaries.Count})",
            RunLogPath = string.Join(Environment.NewLine, summaries.Where(summary => !summary.DryRun).Select(summary => summary.RunLogPath)),
            StartTime = summaries.Min(summary => summary.StartTime),
            StopTime = summaries.Max(summary => summary.StopTime ?? summary.StartTime),
            DryRun = summaries.All(summary => summary.DryRun),
            ImagesFound = summaries.Sum(summary => summary.ImagesFound),
            Completed = summaries.Sum(summary => summary.Completed),
            Skipped = summaries.Sum(summary => summary.Skipped),
            Failed = summaries.Sum(summary => summary.Failed),
            ParseFailed = summaries.Sum(summary => summary.ParseFailed),
            XmpWritten = summaries.Sum(summary => summary.XmpWritten),
            WouldProcess = summaries.Sum(summary => summary.WouldProcess),
            WouldWriteJsonSidecar = summaries.Sum(summary => summary.WouldWriteJsonSidecar),
            WouldWriteXmpSidecar = summaries.Sum(summary => summary.WouldWriteXmpSidecar),
            WouldSkipJsonSidecar = summaries.Sum(summary => summary.WouldSkipJsonSidecar),
            WouldSkipXmpSidecar = summaries.Sum(summary => summary.WouldSkipXmpSidecar),
            ExistingJsonSidecars = summaries.Sum(summary => summary.ExistingJsonSidecars),
            ExistingXmpSidecars = summaries.Sum(summary => summary.ExistingXmpSidecars),
            JsonWriteSkipped = summaries.Sum(summary => summary.JsonWriteSkipped),
            XmpWriteSkipped = summaries.Sum(summary => summary.XmpWriteSkipped),
            ModelFailures = summaries.Sum(summary => summary.ModelFailures),
            PrimaryRetryAttempts = summaries.Sum(summary => summary.PrimaryRetryAttempts),
            PrimaryRetrySucceeded = summaries.Sum(summary => summary.PrimaryRetrySucceeded),
            PrimaryRetryFailed = summaries.Sum(summary => summary.PrimaryRetryFailed),
            FallbackAttempts = summaries.Sum(summary => summary.FallbackAttempts),
            FallbackSucceeded = summaries.Sum(summary => summary.FallbackSucceeded)
        };
    }
}
