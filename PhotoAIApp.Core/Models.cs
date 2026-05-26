using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoAIApp.Core;

public sealed class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("images")]
    public required string[] Images { get; init; }

    [JsonPropertyName("stream")]
    public required bool Stream { get; init; }

    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; init; }

    [JsonPropertyName("keep_alive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeepAlive { get; init; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Options { get; init; }
}

public sealed class OllamaGenerateResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("response")]
    public string Response { get; init; } = "";

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }

    [JsonPropertyName("total_duration")]
    public long? TotalDurationNanoseconds { get; init; }

    [JsonPropertyName("load_duration")]
    public long? LoadDurationNanoseconds { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; init; }

    [JsonPropertyName("prompt_eval_duration")]
    public long? PromptEvalDurationNanoseconds { get; init; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; init; }

    [JsonPropertyName("eval_duration")]
    public long? EvalDurationNanoseconds { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed class PhotoAiSidecar
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
    public PhotoAiImageProcessingMetadata? ImageProcessing { get; init; }

    [JsonPropertyName("ollama_stats")]
    public PhotoAiOllamaStats? OllamaStats { get; init; }

    [JsonPropertyName("model_attempts")]
    public PhotoAiModelAttemptLog[] ModelAttempts { get; init; } = [];

    [JsonPropertyName("raw_response")]
    public required string RawResponse { get; init; }
}

public sealed class PhotoAiImageProcessingMetadata
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

    [JsonPropertyName("base64_chars")]
    public int Base64Chars { get; init; }

    [JsonPropertyName("resize_elapsed_ms")]
    public double ResizeElapsedMilliseconds { get; init; }
}

public sealed class PhotoAiOllamaStats
{
    [JsonPropertyName("model_returned")]
    public string? ModelReturned { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }

    [JsonPropertyName("http_elapsed_ms")]
    public double HttpElapsedMilliseconds { get; init; }

    [JsonPropertyName("total_duration_ns")]
    public long? TotalDurationNanoseconds { get; init; }

    [JsonPropertyName("load_duration_ns")]
    public long? LoadDurationNanoseconds { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; init; }

    [JsonPropertyName("prompt_eval_duration_ns")]
    public long? PromptEvalDurationNanoseconds { get; init; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; init; }

    [JsonPropertyName("eval_duration_ns")]
    public long? EvalDurationNanoseconds { get; init; }

    [JsonPropertyName("response_chars")]
    public int ResponseChars { get; init; }
}

public sealed class PhotoAiModelAttemptLog
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

    [JsonPropertyName("image_processing")]
    public PhotoAiImageProcessingMetadata? ImageProcessing { get; init; }

    [JsonPropertyName("ollama_stats")]
    public PhotoAiOllamaStats? OllamaStats { get; init; }
}

public sealed class PhotoAnalysis
{
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("short_caption")]
    public string ShortCaption { get; init; } = "";

    [JsonPropertyName("tags")]
    public string[] Tags { get; init; } = [];

    [JsonPropertyName("people_count")]
    public int PeopleCount { get; init; }

    [JsonPropertyName("setting")]
    public string Setting { get; init; } = "unknown";

    [JsonPropertyName("quality_flags")]
    public string[] QualityFlags { get; init; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}


public sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public OllamaModelInfo[] Models { get; init; } = [];
}

public sealed class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}
