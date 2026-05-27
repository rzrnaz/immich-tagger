using Microsoft.Extensions.Configuration;

namespace PhotoAIApp.Core;

public sealed class ImmichTaggerSettings
{
    public const string SectionName = "ImmichTagger";

    [ConfigurationKeyName("PHOTO_ROOT")]
    public string PhotoRoot { get; init; } = "/photos";

    [ConfigurationKeyName("CONFIG_ROOT")]
    public string ConfigRoot { get; init; } = "/config";

    [ConfigurationKeyName("LOG_ROOT")]
    public string LogRoot { get; init; } = "/config/logs";

    [ConfigurationKeyName("DEFAULT_FOLDER_PATH")]
    public string DefaultFolderPath { get; init; } = "/photos";

    [ConfigurationKeyName("RECURSIVE")]
    public bool Recursive { get; init; } = true;

    [ConfigurationKeyName("FORCE")]
    public bool Force { get; init; } = true;

    [ConfigurationKeyName("WRITE_JSON")]
    public bool WriteJson { get; init; } = true;

    [ConfigurationKeyName("WRITE_XMP")]
    public bool WriteXmp { get; init; } = true;

    [ConfigurationKeyName("ADD_TAGS")]
    public bool AddTags { get; init; } = true;

    [ConfigurationKeyName("DRY_RUN_DEFAULT")]
    public bool DryRunDefault { get; init; } = true;

    [ConfigurationKeyName("OVERWRITE_SIDECARS")]
    public bool OverwriteSidecars { get; init; }

    [ConfigurationKeyName("LIMIT")]
    public int? Limit { get; init; }

    [ConfigurationKeyName("PRIMARY_OLLAMA_URL")]
    public string PrimaryOllamaUrl { get; init; } = PhotoAiDefaults.UnraidOllamaBaseUrl;

    [ConfigurationKeyName("PRIMARY_MODEL")]
    public string PrimaryModel { get; init; } = PhotoAiDefaults.QwenPcModel;

    [ConfigurationKeyName("MAX_IMAGE_SIZE")]
    public int MaxImageSize { get; init; } = 0;

    [ConfigurationKeyName("FALLBACK_ENABLED")]
    public bool FallbackEnabled { get; init; } = true;

    [ConfigurationKeyName("FALLBACK_OLLAMA_URL")]
    public string FallbackOllamaUrl { get; init; } = PhotoAiDefaults.UnraidOllamaBaseUrl;

    [ConfigurationKeyName("FALLBACK_MODEL")]
    public string FallbackModel { get; init; } = PhotoAiDefaults.UnraidModel;

    [ConfigurationKeyName("FALLBACK_MAX_IMAGE_SIZE")]
    public int FallbackMaxImageSize { get; init; } = PhotoAiDefaults.UnraidMaxImageDimensionPixels;

    public PhotoAiScanOptions ToScanOptions(string folderPath, bool dryRun, int? limit = null)
    {
        return new PhotoAiScanOptions
        {
            FolderPath = string.IsNullOrWhiteSpace(folderPath) ? DefaultFolderPath : folderPath,
            SafetyRootPath = PhotoRoot,
            Recursive = Recursive,
            Force = Force,
            WriteJson = WriteJson,
            WriteXmp = WriteXmp,
            OverwriteJson = OverwriteSidecars,
            OverwriteXmp = OverwriteSidecars,
            AddTags = AddTags,
            DryRun = dryRun,
            OllamaBaseUrl = PrimaryOllamaUrl,
            Model = PrimaryModel,
            ModelPreference = FallbackEnabled
                ? PhotoAiModelPreference.QwenPcWithUnraidFallback
                : PhotoAiModelPreference.PrimaryOnly,
            FallbackOllamaBaseUrl = FallbackOllamaUrl,
            FallbackModel = FallbackModel,
            MaxImageDimensionPixels = MaxImageSize,
            FallbackMaxImageDimensionPixels = FallbackMaxImageSize,
            Limit = limit ?? Limit
        };
    }
}
