namespace PhotoAIApp.Core;

public enum PhotoAiModelProfileId
{
    HighQuality,
    Balanced,
    Compatibility,
    Custom
}

public sealed record PhotoAiModelProfile
{
    public PhotoAiModelProfileId ProfileId { get; init; } = PhotoAiModelProfileId.Custom;
    public string DisplayName { get; init; } = "Custom";
    public string OllamaBaseUrl { get; init; } = PhotoAiDefaults.QwenPcOllamaBaseUrl;
    public string Model { get; init; } = PhotoAiDefaults.QwenPcModel;
    public int MaxImageDimensionPixels { get; init; } = PhotoAiDefaults.QwenMaxImageDimensionPixels;
    public PhotoAiModelPreference ModelPreference { get; init; } = PhotoAiModelPreference.QwenPcWithUnraidFallback;
    public string FallbackOllamaBaseUrl { get; init; } = PhotoAiDefaults.UnraidOllamaBaseUrl;
    public string FallbackModel { get; init; } = PhotoAiDefaults.UnraidModel;
    public int FallbackMaxImageDimensionPixels { get; init; } = PhotoAiDefaults.UnraidMaxImageDimensionPixels;

    public string ImageEdgeLabel => MaxImageDimensionPixels <= 0
        ? "Original/full-res"
        : $"{MaxImageDimensionPixels}px";

    public string Summary => $"Target: {FormatEndpoint(OllamaBaseUrl)} | Model: {Model} | Max Image Size: {ImageEdgeLabel} | Fallback: {FormatFallback()}";

    public static IReadOnlyList<PhotoAiModelProfile> Presets { get; } =
    [
        new PhotoAiModelProfile
        {
            ProfileId = PhotoAiModelProfileId.HighQuality,
            DisplayName = "High Quality - Qwen 7B full-res",
            OllamaBaseUrl = PhotoAiDefaults.QwenPcOllamaBaseUrl,
            Model = PhotoAiDefaults.QwenPcModel,
            MaxImageDimensionPixels = 0,
            ModelPreference = PhotoAiModelPreference.QwenPcWithUnraidFallback,
            FallbackOllamaBaseUrl = PhotoAiDefaults.UnraidOllamaBaseUrl,
            FallbackModel = PhotoAiDefaults.UnraidModel,
            FallbackMaxImageDimensionPixels = PhotoAiDefaults.UnraidMaxImageDimensionPixels
        },
        new PhotoAiModelProfile
        {
            ProfileId = PhotoAiModelProfileId.Balanced,
            DisplayName = "Balanced - Qwen 7B 1440px",
            OllamaBaseUrl = PhotoAiDefaults.QwenPcOllamaBaseUrl,
            Model = PhotoAiDefaults.QwenPcModel,
            MaxImageDimensionPixels = PhotoAiDefaults.QwenMaxImageDimensionPixels,
            ModelPreference = PhotoAiModelPreference.QwenPcWithUnraidFallback,
            FallbackOllamaBaseUrl = PhotoAiDefaults.UnraidOllamaBaseUrl,
            FallbackModel = PhotoAiDefaults.UnraidModel,
            FallbackMaxImageDimensionPixels = PhotoAiDefaults.UnraidMaxImageDimensionPixels
        },
        new PhotoAiModelProfile
        {
            ProfileId = PhotoAiModelProfileId.Compatibility,
            DisplayName = "Compatibility - Unraid MiniCPM-V",
            OllamaBaseUrl = PhotoAiDefaults.UnraidOllamaBaseUrl,
            Model = PhotoAiDefaults.UnraidModel,
            MaxImageDimensionPixels = 0,
            ModelPreference = PhotoAiModelPreference.UnraidMiniCpmOnly,
            FallbackOllamaBaseUrl = PhotoAiDefaults.UnraidOllamaBaseUrl,
            FallbackModel = PhotoAiDefaults.UnraidModel,
            FallbackMaxImageDimensionPixels = PhotoAiDefaults.UnraidMaxImageDimensionPixels
        }
    ];

    public static PhotoAiModelProfile GetPreset(PhotoAiModelProfileId profileId)
    {
        return Presets.FirstOrDefault(profile => profile.ProfileId == profileId)
            ?? GetPreset(PhotoAiModelProfileId.HighQuality);
    }

    public static PhotoAiModelProfileId MatchPreset(PhotoAiModelProfile profile)
    {
        foreach (PhotoAiModelProfile preset in Presets)
        {
            if (ProfileSettingsEqual(profile, preset))
            {
                return preset.ProfileId;
            }
        }

        return PhotoAiModelProfileId.Custom;
    }

    public static bool ProfileSettingsEqual(PhotoAiModelProfile left, PhotoAiModelProfile right)
    {
        return string.Equals(NormalizeUrl(left.OllamaBaseUrl), NormalizeUrl(right.OllamaBaseUrl), StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Model.Trim(), right.Model.Trim(), StringComparison.OrdinalIgnoreCase)
            && left.MaxImageDimensionPixels == right.MaxImageDimensionPixels
            && left.ModelPreference == right.ModelPreference
            && string.Equals(NormalizeUrl(left.FallbackOllamaBaseUrl), NormalizeUrl(right.FallbackOllamaBaseUrl), StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.FallbackModel.Trim(), right.FallbackModel.Trim(), StringComparison.OrdinalIgnoreCase)
            && left.FallbackMaxImageDimensionPixels == right.FallbackMaxImageDimensionPixels;
    }

    public static string BuildBaseUrl(string hostOrUrl, int port)
    {
        string trimmed = string.IsNullOrWhiteSpace(hostOrUrl) ? "localhost" : hostOrUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return NormalizeUrl(absoluteUri.ToString());
        }

        trimmed = trimmed.TrimEnd('/');
        return $"http://{trimmed}:{port}";
    }

    public static string ExtractHost(string baseUrl)
    {
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri)
            ? uri.Host
            : baseUrl.Trim();
    }

    public static int ExtractPort(string baseUrl, int fallbackPort = 11434)
    {
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) && !uri.IsDefaultPort
            ? uri.Port
            : fallbackPort;
    }

    public PhotoAiScanOptions ApplyTo(PhotoAiScanOptions options)
    {
        return new PhotoAiScanOptions
        {
            FolderPath = options.FolderPath,
            SafetyRootPath = options.SafetyRootPath,
            Recursive = options.Recursive,
            Force = options.Force,
            WriteJson = options.WriteJson,
            WriteXmp = options.WriteXmp,
            OverwriteJson = options.OverwriteJson,
            OverwriteXmp = options.OverwriteXmp,
            AddTags = options.AddTags,
            DryRun = options.DryRun,
            OllamaBaseUrl = OllamaBaseUrl,
            Model = Model,
            ModelPreference = ModelPreference,
            FallbackOllamaBaseUrl = FallbackOllamaBaseUrl,
            FallbackModel = FallbackModel,
            FallbackMaxImageDimensionPixels = FallbackMaxImageDimensionPixels,
            PauseController = options.PauseController,
            MaxImageDimensionPixels = MaxImageDimensionPixels,
            Limit = options.Limit,
            UnloadModelsAtEnd = options.UnloadModelsAtEnd,
            ProgressCompletedOffset = options.ProgressCompletedOffset,
            ProgressSkippedOffset = options.ProgressSkippedOffset,
            ProgressFailedOffset = options.ProgressFailedOffset,
            ProgressTotalFiles = options.ProgressTotalFiles,
            ProgressStartedAt = options.ProgressStartedAt
        };
    }

    private string FormatFallback()
    {
        return ModelPreference == PhotoAiModelPreference.QwenPcWithUnraidFallback
            ? $"{FallbackModel} @ {FormatEndpoint(FallbackOllamaBaseUrl)} ({FormatImageEdge(FallbackMaxImageDimensionPixels)})"
            : "none";
    }

    private static string FormatImageEdge(int maxImageDimensionPixels)
    {
        return maxImageDimensionPixels <= 0 ? "Original/full-res" : $"{maxImageDimensionPixels}px";
    }

    private static string FormatEndpoint(string ollamaBaseUrl)
    {
        if (Uri.TryCreate(ollamaBaseUrl, UriKind.Absolute, out Uri? uri))
        {
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }

        return ollamaBaseUrl;
    }

    private static string NormalizeUrl(string url)
    {
        return url.Trim().TrimEnd('/');
    }
}

public sealed class PhotoAiGuiSettings
{
    public PhotoAiModelProfileId SelectedProfileId { get; init; } = PhotoAiModelProfileId.HighQuality;
    public PhotoAiModelProfile CustomProfile { get; init; } = PhotoAiModelProfile.GetPreset(PhotoAiModelProfileId.HighQuality);

    public PhotoAiModelProfile EffectiveProfile => SelectedProfileId == PhotoAiModelProfileId.Custom
        ? CustomProfile with { ProfileId = PhotoAiModelProfileId.Custom, DisplayName = "Custom" }
        : PhotoAiModelProfile.GetPreset(SelectedProfileId);

    public PhotoAiGuiSettings WithSelectedProfile(PhotoAiModelProfileId selectedProfileId, PhotoAiModelProfile profile)
    {
        return new PhotoAiGuiSettings
        {
            SelectedProfileId = selectedProfileId,
            CustomProfile = selectedProfileId == PhotoAiModelProfileId.Custom
                ? profile with { ProfileId = PhotoAiModelProfileId.Custom, DisplayName = "Custom" }
                : CustomProfile
        };
    }

    public PhotoAiGuiSettings WithEffectiveProfile(PhotoAiModelProfile profile)
    {
        PhotoAiModelProfileId matched = PhotoAiModelProfile.MatchPreset(profile);
        return new PhotoAiGuiSettings
        {
            SelectedProfileId = matched,
            CustomProfile = matched == PhotoAiModelProfileId.Custom
                ? profile with { ProfileId = PhotoAiModelProfileId.Custom, DisplayName = "Custom" }
                : PhotoAiModelProfile.GetPreset(matched)
        };
    }
}
