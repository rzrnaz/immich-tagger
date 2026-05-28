using Microsoft.Extensions.Configuration;

namespace PhotoAIApp.Core;

public sealed class ImmichTaggerSettings
{
    public const string SectionName = "ImmichTagger";

    [ConfigurationKeyName("PHOTO_ROOT")]
    public string PhotoRoot { get; set; } = "/photos";

    [ConfigurationKeyName("CONFIG_ROOT")]
    public string ConfigRoot { get; set; } = "/config";

    [ConfigurationKeyName("LOG_ROOT")]
    public string LogRoot { get; set; } = "/config/logs";

    [ConfigurationKeyName("DEFAULT_FOLDER_PATH")]
    public string DefaultFolderPath { get; set; } = "/photos";

    [ConfigurationKeyName("RECURSIVE")]
    public bool Recursive { get; set; } = true;

    [ConfigurationKeyName("FORCE")]
    public bool Force { get; set; } = true;

    [ConfigurationKeyName("WRITE_JSON")]
    public bool WriteJson { get; set; } = false;

    [ConfigurationKeyName("WRITE_XMP")]
    public bool WriteXmp { get; set; } = true;

    [ConfigurationKeyName("ADD_TAGS")]
    public bool AddTags { get; set; } = true;

    [ConfigurationKeyName("DRY_RUN_DEFAULT")]
    public bool DryRunDefault { get; set; } = true;

    [ConfigurationKeyName("OVERWRITE_SIDECARS")]
    public bool OverwriteSidecars { get; set; }

    [ConfigurationKeyName("LIMIT")]
    public int? Limit { get; set; }

    [ConfigurationKeyName("PRIMARY_OLLAMA_URL")]
    public string PrimaryOllamaUrl { get; set; } = PhotoAiDefaults.UnraidOllamaBaseUrl;

    [ConfigurationKeyName("PRIMARY_MODEL")]
    public string PrimaryModel { get; set; } = PhotoAiDefaults.QwenPcModel;

    [ConfigurationKeyName("MAX_IMAGE_SIZE")]
    public int MaxImageSize { get; set; } = 0;

    [ConfigurationKeyName("FALLBACK_ENABLED")]
    public bool FallbackEnabled { get; set; } = true;

    [ConfigurationKeyName("FALLBACK_OLLAMA_URL")]
    public string FallbackOllamaUrl { get; set; } = PhotoAiDefaults.UnraidOllamaBaseUrl;

    [ConfigurationKeyName("FALLBACK_MODEL")]
    public string FallbackModel { get; set; } = PhotoAiDefaults.UnraidModel;

    [ConfigurationKeyName("FALLBACK_MAX_IMAGE_SIZE")]
    public int FallbackMaxImageSize { get; set; } = PhotoAiDefaults.UnraidMaxImageDimensionPixels;

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
