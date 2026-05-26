using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoAIApp.Core;

public static class PhotoAiSidecarSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(PhotoAiSidecar sidecar)
    {
        return JsonSerializer.Serialize(ToSerializableSidecar(sidecar), Options);
    }

    private static SerializableSidecar ToSerializableSidecar(PhotoAiSidecar sidecar)
    {
        return new SerializableSidecar
        {
            SourceFile = sidecar.SourceFile,
            SourceFileName = sidecar.SourceFileName,
            SourceRoot = sidecar.SourceRoot,
            RelativePath = sidecar.RelativePath,
            OllamaBaseUrl = sidecar.OllamaBaseUrl,
            Model = sidecar.Model,
            ProcessedAtUtc = sidecar.ProcessedAtUtc,
            Analysis = sidecar.Analysis,
            ImageProcessing = ToCompactImageProcessing(sidecar.ImageProcessing),
            OllamaStats = sidecar.OllamaStats,
            ModelAttempts = sidecar.ModelAttempts.Select(ToCompactAttempt).ToArray(),
            RawResponse = sidecar.RawResponse
        };
    }

    private static CompactImageProcessingMetadata? ToCompactImageProcessing(PhotoAiImageProcessingMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return new CompactImageProcessingMetadata
        {
            OriginalWidth = metadata.OriginalWidth,
            OriginalHeight = metadata.OriginalHeight,
            SentWidth = metadata.SentWidth,
            SentHeight = metadata.SentHeight,
            WasResized = metadata.WasResized,
            MaxImageDimensionPixels = metadata.MaxImageDimensionPixels,
            OriginalBytes = metadata.OriginalBytes,
            SentBytes = metadata.SentBytes
        };
    }

    private static CompactModelAttemptLog ToCompactAttempt(PhotoAiModelAttemptLog attempt)
    {
        return new CompactModelAttemptLog
        {
            AttemptNumber = attempt.AttemptNumber,
            OllamaBaseUrl = attempt.OllamaBaseUrl,
            Model = attempt.Model,
            IsFallback = attempt.IsFallback,
            StartedAtUtc = attempt.StartedAtUtc,
            CompletedAtUtc = attempt.CompletedAtUtc,
            Succeeded = attempt.Succeeded,
            TimeoutSeconds = attempt.TimeoutSeconds,
            ErrorType = attempt.ErrorType,
            ErrorMessage = attempt.ErrorMessage
        };
    }

    private sealed class SerializableSidecar
    {
        [JsonPropertyName("source_file")]
        public required string SourceFile { get; init; }

        [JsonPropertyName("source_file_name")]
        public required string SourceFileName { get; init; }

        [JsonPropertyName("source_root")]
        public required string SourceRoot { get; init; }

        [JsonPropertyName("relative_path")]
        public required string RelativePath { get; init; }

        [JsonPropertyName("ollama_base_url")]
        public required string OllamaBaseUrl { get; init; }

        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("processed_at_utc")]
        public required DateTimeOffset ProcessedAtUtc { get; init; }

        [JsonPropertyName("analysis")]
        public PhotoAnalysis? Analysis { get; init; }

        [JsonPropertyName("image_processing")]
        public CompactImageProcessingMetadata? ImageProcessing { get; init; }

        [JsonPropertyName("ollama_stats")]
        public PhotoAiOllamaStats? OllamaStats { get; init; }

        [JsonPropertyName("model_attempts")]
        public CompactModelAttemptLog[] ModelAttempts { get; init; } = [];

        [JsonPropertyName("raw_response")]
        public required string RawResponse { get; init; }
    }

    private sealed class CompactImageProcessingMetadata
    {
        [JsonPropertyName("original_width")]
        public int OriginalWidth { get; init; }

        [JsonPropertyName("original_height")]
        public int OriginalHeight { get; init; }

        [JsonPropertyName("sent_width")]
        public int SentWidth { get; init; }

        [JsonPropertyName("sent_height")]
        public int SentHeight { get; init; }

        [JsonPropertyName("was_resized")]
        public bool WasResized { get; init; }

        [JsonPropertyName("max_image_dimension_pixels")]
        public int MaxImageDimensionPixels { get; init; }

        [JsonPropertyName("original_bytes")]
        public long OriginalBytes { get; init; }

        [JsonPropertyName("sent_bytes")]
        public long SentBytes { get; init; }
    }

    private sealed class CompactModelAttemptLog
    {
        [JsonPropertyName("attempt_number")]
        public int AttemptNumber { get; init; }

        [JsonPropertyName("ollama_base_url")]
        public string OllamaBaseUrl { get; init; } = "";

        [JsonPropertyName("model")]
        public string Model { get; init; } = "";

        [JsonPropertyName("is_fallback")]
        public bool IsFallback { get; init; }

        [JsonPropertyName("started_at_utc")]
        public DateTimeOffset StartedAtUtc { get; init; }

        [JsonPropertyName("completed_at_utc")]
        public DateTimeOffset? CompletedAtUtc { get; init; }

        [JsonPropertyName("succeeded")]
        public bool Succeeded { get; init; }

        [JsonPropertyName("timeout_seconds")]
        public double TimeoutSeconds { get; init; }

        [JsonPropertyName("error_type")]
        public string? ErrorType { get; init; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; init; }
    }
}
