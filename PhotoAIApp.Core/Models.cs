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
    public string? Format { get; init; }
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

    [JsonPropertyName("raw_response")]
    public required string RawResponse { get; init; }
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
