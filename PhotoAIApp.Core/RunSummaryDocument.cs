using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoAIApp.Core;

public sealed record PhotoAiRunSummaryDocument
{
    public required string Title { get; init; }
    public required PhotoAiRunState State { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string PlainText { get; init; }
    public required string Json { get; init; }
    public required string SuggestedFileName { get; init; }

    public string SuggestedJsonFileName => Path.ChangeExtension(SuggestedFileName, ".json");

    public static PhotoAiRunSummaryDocument FromSummary(PhotoAiScanSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        PhotoAiRunState state = summary.StopTime is null
            ? PhotoAiRunState.Running
            : PhotoAiRunState.Completed;

        string mode = summary.DryRun ? "Dry run" : "Live run";
        string title = summary.DryRun
            ? "PhotoAI Run Summary - Dry Run"
            : "PhotoAI Run Summary";

        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine(new string('=', title.Length));
        builder.AppendLine();
        builder.AppendLine($"Mode: {mode}");
        builder.AppendLine($"State: {state}");
        builder.AppendLine($"Root: {summary.RootPath}");
        builder.AppendLine($"Start: {FormatTimestamp(summary.StartTime)}");
        builder.AppendLine($"Stop: {FormatTimestamp(summary.StopTime)}");
        builder.AppendLine($"Elapsed: {FormatDuration(summary.ElapsedTime)}");
        builder.AppendLine($"Images found: {summary.ImagesFound}");
        builder.AppendLine($"Average/photo: {FormatDuration(summary.AverageTimePerProcessedPhoto)}");
        builder.AppendLine($"Anomaly log: {summary.RunLogPath}");
        builder.AppendLine();

        if (summary.DryRun)
        {
            AppendSection(builder, "Dry-run preview", [
                $"Would process: {summary.WouldProcess}",
                $"Would write JSON: {summary.WouldWriteJsonSidecar}",
                $"Would write XMP: {summary.WouldWriteXmpSidecar}",
                $"Would skip JSON: {summary.WouldSkipJsonSidecar}",
                $"Would skip XMP: {summary.WouldSkipXmpSidecar}",
                $"Existing JSON: {summary.ExistingJsonSidecars}",
                $"Existing XMP: {summary.ExistingXmpSidecars}",
                $"Blocked/failed: {summary.Failed}"
            ]);
        }
        else
        {
            AppendSection(builder, "Results", [
                $"Files processed: {summary.Completed}",
                $"XMP files successfully written: {summary.XmpWritten}",
                $"Skipped: {summary.Skipped}",
                $"Failed: {summary.Failed}",
                $"Parse/XMP skipped: {summary.ParseFailed}",
                $"JSON write skipped: {summary.JsonWriteSkipped}",
                $"XMP write skipped: {summary.XmpWriteSkipped}",
                $"Model failures: {summary.ModelFailures}",
                $"Fallback attempts: {summary.FallbackAttempts}",
                $"Fallback successes: {summary.FallbackSucceeded}"
            ]);
        }

        DateTimeOffset createdAt = DateTimeOffset.Now;
        string suggestedFileName = BuildSuggestedFileName(summary, createdAt, ".txt");
        string json = JsonSerializer.Serialize(
            PhotoAiRunSummaryJson.FromSummary(summary, title, state, createdAt),
            new JsonSerializerOptions { WriteIndented = true });

        return new PhotoAiRunSummaryDocument
        {
            Title = title,
            State = state,
            CreatedAt = createdAt,
            PlainText = builder.ToString(),
            Json = json,
            SuggestedFileName = suggestedFileName
        };
    }

    public void SaveAs(string path)
    {
        SaveTextAs(path);
    }

    public void SaveTextAs(string path)
    {
        WriteAllTextCreatingDirectory(path, PlainText);
    }

    public void SaveJsonAs(string path)
    {
        WriteAllTextCreatingDirectory(path, Json);
    }

    private static void WriteAllTextCreatingDirectory(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Summary path is required.", nameof(path));
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    private static void AppendSection(StringBuilder builder, string heading, IEnumerable<string> lines)
    {
        builder.AppendLine(heading);
        builder.AppendLine(new string('-', heading.Length));
        foreach (string line in lines)
        {
            builder.AppendLine(line);
        }
    }

    private static string BuildSuggestedFileName(PhotoAiScanSummary summary, DateTimeOffset createdAt, string extension)
    {
        string mode = summary.DryRun ? "dry-run" : "run";
        string timestamp = (summary.StopTime ?? createdAt).ToLocalTime().ToString("yyyyMMdd-HHmmss");
        string rawFileName = $"photoai-{mode}-summary-{timestamp}{extension}";
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            rawFileName = rawFileName.Replace(invalid, '-');
        }

        return rawFileName;
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp is null
            ? "n/a"
            : timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private sealed record PhotoAiRunSummaryJson
    {
        [JsonPropertyName("title")]
        public required string Title { get; init; }

        [JsonPropertyName("state")]
        public required string State { get; init; }

        [JsonPropertyName("created_at")]
        public required DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("mode")]
        public required string Mode { get; init; }

        [JsonPropertyName("root_path")]
        public required string RootPath { get; init; }

        [JsonPropertyName("run_log_path")]
        public required string RunLogPath { get; init; }

        [JsonPropertyName("start_time")]
        public required DateTimeOffset StartTime { get; init; }

        [JsonPropertyName("stop_time")]
        public DateTimeOffset? StopTime { get; init; }

        [JsonPropertyName("elapsed_seconds")]
        public required double ElapsedSeconds { get; init; }

        [JsonPropertyName("average_seconds_per_processed_photo")]
        public required double AverageSecondsPerProcessedPhoto { get; init; }

        [JsonPropertyName("images_found")]
        public required int ImagesFound { get; init; }

        [JsonPropertyName("completed")]
        public required int Completed { get; init; }

        [JsonPropertyName("skipped")]
        public required int Skipped { get; init; }

        [JsonPropertyName("failed")]
        public required int Failed { get; init; }

        [JsonPropertyName("parse_failed")]
        public required int ParseFailed { get; init; }

        [JsonPropertyName("xmp_written")]
        public required int XmpWritten { get; init; }

        [JsonPropertyName("json_write_skipped")]
        public required int JsonWriteSkipped { get; init; }

        [JsonPropertyName("xmp_write_skipped")]
        public required int XmpWriteSkipped { get; init; }

        [JsonPropertyName("model_failures")]
        public required int ModelFailures { get; init; }

        [JsonPropertyName("fallback_attempts")]
        public required int FallbackAttempts { get; init; }

        [JsonPropertyName("fallback_successes")]
        public required int FallbackSuccesses { get; init; }

        [JsonPropertyName("would_process")]
        public required int WouldProcess { get; init; }

        [JsonPropertyName("would_write_json")]
        public required int WouldWriteJson { get; init; }

        [JsonPropertyName("would_write_xmp")]
        public required int WouldWriteXmp { get; init; }

        [JsonPropertyName("would_skip_json")]
        public required int WouldSkipJson { get; init; }

        [JsonPropertyName("would_skip_xmp")]
        public required int WouldSkipXmp { get; init; }

        [JsonPropertyName("existing_json")]
        public required int ExistingJson { get; init; }

        [JsonPropertyName("existing_xmp")]
        public required int ExistingXmp { get; init; }

        public static PhotoAiRunSummaryJson FromSummary(
            PhotoAiScanSummary summary,
            string title,
            PhotoAiRunState state,
            DateTimeOffset createdAt)
        {
            return new PhotoAiRunSummaryJson
            {
                Title = title,
                State = state.ToString(),
                CreatedAt = createdAt,
                Mode = summary.DryRun ? "dry_run" : "live_run",
                RootPath = summary.RootPath,
                RunLogPath = summary.RunLogPath,
                StartTime = summary.StartTime,
                StopTime = summary.StopTime,
                ElapsedSeconds = summary.ElapsedTime.TotalSeconds,
                AverageSecondsPerProcessedPhoto = summary.AverageTimePerProcessedPhoto.TotalSeconds,
                ImagesFound = summary.ImagesFound,
                Completed = summary.Completed,
                Skipped = summary.Skipped,
                Failed = summary.Failed,
                ParseFailed = summary.ParseFailed,
                XmpWritten = summary.XmpWritten,
                JsonWriteSkipped = summary.JsonWriteSkipped,
                XmpWriteSkipped = summary.XmpWriteSkipped,
                ModelFailures = summary.ModelFailures,
                FallbackAttempts = summary.FallbackAttempts,
                FallbackSuccesses = summary.FallbackSucceeded,
                WouldProcess = summary.WouldProcess,
                WouldWriteJson = summary.WouldWriteJsonSidecar,
                WouldWriteXmp = summary.WouldWriteXmpSidecar,
                WouldSkipJson = summary.WouldSkipJsonSidecar,
                WouldSkipXmp = summary.WouldSkipXmpSidecar,
                ExistingJson = summary.ExistingJsonSidecars,
                ExistingXmp = summary.ExistingXmpSidecars
            };
        }
    }
}
