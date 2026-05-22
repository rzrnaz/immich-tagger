namespace PhotoAIApp.Core;

public enum PhotoAiRunState
{
    Ready,
    Scanning,
    DryRunning,
    Running,
    Paused,
    Cancelling,
    Cancelled,
    Completed,
    Failed
}

public sealed record PhotoAiRunProgressSnapshot
{
    public const int MinimumCompletedFilesForEstimate = 10;

    public required PhotoAiRunState State { get; init; }
    public required string Phase { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset Now { get; init; }
    public required int? TotalFiles { get; init; }
    public required int CompletedFiles { get; init; }
    public required int SkippedFiles { get; init; }
    public required int FailedFiles { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required TimeSpan? EstimatedRemaining { get; init; }
    public required DateTimeOffset? EstimatedFinishTime { get; init; }

    public int FilesFinished => CompletedFiles + SkippedFiles + FailedFiles;
    public bool IsIndeterminate => TotalFiles is null or <= 0;

    /// <summary>
    /// Gets overall progress as a percentage in the 0..100 range.
    /// </summary>
    public double? ProgressFraction => IsIndeterminate
        ? null
        : Math.Clamp(FilesFinished * 100.0 / TotalFiles!.Value, 0.0, 100.0);

    public string EstimatedRemainingDisplay => EstimatedRemaining is null
        ? "Estimating..."
        : FormatDuration(EstimatedRemaining.Value);

    public string EstimatedFinishTimeDisplay => EstimatedFinishTime is null
        ? "Estimating..."
        : EstimatedFinishTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public static PhotoAiRunProgressSnapshot Create(
        PhotoAiRunState state,
        string phase,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        int? totalFiles,
        int completedFiles,
        int skippedFiles,
        int failedFiles)
    {
        if (now < startedAt)
        {
            now = startedAt;
        }

        completedFiles = Math.Max(0, completedFiles);
        skippedFiles = Math.Max(0, skippedFiles);
        failedFiles = Math.Max(0, failedFiles);
        totalFiles = totalFiles is null ? null : Math.Max(0, totalFiles.Value);

        TimeSpan elapsed = now - startedAt;
        int filesFinished = completedFiles + skippedFiles + failedFiles;
        int remainingFiles = totalFiles is null
            ? 0
            : Math.Max(0, totalFiles.Value - filesFinished);

        TimeSpan? estimatedRemaining = null;
        DateTimeOffset? estimatedFinishTime = null;

        if (completedFiles >= MinimumCompletedFilesForEstimate && remainingFiles > 0 && elapsed > TimeSpan.Zero)
        {
            double averageSecondsPerCompletedFile = elapsed.TotalSeconds / completedFiles;
            estimatedRemaining = TimeSpan.FromSeconds(averageSecondsPerCompletedFile * remainingFiles);
            estimatedFinishTime = now + estimatedRemaining.Value;
        }

        return new PhotoAiRunProgressSnapshot
        {
            State = state,
            Phase = string.IsNullOrWhiteSpace(phase) ? state.ToString() : phase.Trim(),
            StartedAt = startedAt,
            Now = now,
            TotalFiles = totalFiles,
            CompletedFiles = completedFiles,
            SkippedFiles = skippedFiles,
            FailedFiles = failedFiles,
            Elapsed = elapsed,
            EstimatedRemaining = estimatedRemaining,
            EstimatedFinishTime = estimatedFinishTime
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
