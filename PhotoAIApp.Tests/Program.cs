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

Console.WriteLine("PhotoAIApp.Tests passed.");
