namespace SnapCat.Core.Models;

public static class CaptureStartupMode
{
    public const string Snapshot = "snapshot";

    public const string Live = "live";

    public static string Normalize(string? value)
    {
        return string.Equals(value?.Trim(), Live, StringComparison.OrdinalIgnoreCase)
            ? Live
            : Snapshot;
    }
}

public sealed class AppSettings
{
    public const string DefaultSystemPrompt =
        "You are a translation engine. Translate the provided text accurately and naturally. Return translation only.";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string SystemPrompt { get; set; } = DefaultSystemPrompt;

    public bool EnableApiContext { get; set; }

    public List<ApiTranslationProfile> ApiProfiles { get; set; } = [];

    public string SelectedApiProfileId { get; set; } = string.Empty;

    // AI provider credentials remain in the user-local settings store and are never project data.
    public List<AiProviderProfile> AiProviderProfiles { get; set; } = [];

    public string SelectedAiProviderProfileId { get; set; } = string.Empty;

    // Image generation backends are independent from visual-analysis providers.
    public List<ImageGenerationProfile> ImageGenerationProfiles { get; set; } = [];

    public string SelectedImageGenerationProfileId { get; set; } = string.Empty;

    public string TargetLanguage { get; set; } = "zh-CN";

    public string TranslationProviderPreference { get; set; } = SnapCat.Core.Models.TranslationProviderPreference.Local;

    public string OcrEngine { get; set; } = "windows-text-extractor";

    public string TesseractExecutablePath { get; set; } = string.Empty;

    public string TesseractLanguage { get; set; } = "chi_sim+eng";

    public double Temperature { get; set; } = 0.2d;

    public string HotkeyCaptureAndPin { get; set; } = "Ctrl+Shift+1";

    public string HotkeyCaptureAndOcr { get; set; } = string.Empty;

    public string HotkeyCaptureAndTranslate { get; set; } = "Ctrl+Shift+2";

    public string HotkeyCaptureAndWaitForAction { get; set; } = "Ctrl+Shift+3";

    public string HotkeyCaptureAndSave { get; set; } = "Ctrl+Shift+4";

    public string HotkeyCaptureAndCopy { get; set; } = string.Empty;

    public string HotkeyCaptureAndAnnotate { get; set; } = string.Empty;

    public string HotkeyCaptureAndVisualPrompt { get; set; } = string.Empty;

    public string HotkeyFullScreenCanvasEdit { get; set; } = string.Empty;

    public string PinnedCloseShortcut { get; set; } = "Esc";

    public string PinnedHideShortcut { get; set; } = "鼠标中键";

    public string HotkeyShowAllPinned { get; set; } = string.Empty;

    public string HotkeyHideAllPinned { get; set; } = string.Empty;

    public string HotkeyShowUngroupedPinned { get; set; } = string.Empty;

    public string HotkeyShowPinnedGroupOne { get; set; } = string.Empty;

    public string HotkeyShowPinnedGroupTwo { get; set; } = string.Empty;

    public string HotkeyShowPinnedGroupThree { get; set; } = string.Empty;

    public string HotkeyShowMainWindow { get; set; } = string.Empty;

    public string HotkeyExitApplication { get; set; } = string.Empty;

    public string TrayLeftClickAction { get; set; } = nameof(CaptureWorkflowKind.CaptureAndWaitForAction);

    public string TrayTooltipWorkflowOne { get; set; } = nameof(CaptureWorkflowKind.CaptureAndTranslate);

    public string TrayTooltipWorkflowTwo { get; set; } = nameof(CaptureWorkflowKind.CaptureAndPin);

    public string CaptureStartupMode { get; set; } = SnapCat.Core.Models.CaptureStartupMode.Snapshot;

    public string ThemeId { get; set; } = "ocean-blue";

    public int TempFileRetentionDays { get; set; } = 5;

    public int HistoryRetentionDays { get; set; } = 30;

    public bool LaunchAtStartup { get; set; }

    public bool AutoCheckUpdates { get; set; } = true;

    public bool HasAnyApiProfile()
    {
        return ApiProfiles.Any(profile =>
            !string.IsNullOrWhiteSpace(profile.ApiKey)
            || !string.IsNullOrWhiteSpace(profile.Model)
            || !string.IsNullOrWhiteSpace(profile.BaseUrl));
    }

    public ApiTranslationProfile? GetSelectedApiProfile()
    {
        if (ApiProfiles.Count == 0)
        {
            return null;
        }

        var selected = ApiProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, SelectedApiProfileId, StringComparison.Ordinal));

        return selected ?? ApiProfiles[0];
    }

    public void NormalizeApiProfiles()
    {
        if (ApiProfiles.Count == 0
            && (!string.IsNullOrWhiteSpace(ApiKey)
                || !string.IsNullOrWhiteSpace(Model)
                || !string.IsNullOrWhiteSpace(BaseUrl)))
        {
            ApiProfiles.Add(new ApiTranslationProfile
            {
                Name = "默认 API 配置",
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                Model = Model,
                SystemPrompt = string.IsNullOrWhiteSpace(SystemPrompt) ? DefaultSystemPrompt : SystemPrompt,
                EnableContext = EnableApiContext
            });
        }

        for (var index = 0; index < ApiProfiles.Count; index++)
        {
            var profile = ApiProfiles[index];
            profile.Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id.Trim();
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? $"API 配置 {index + 1}" : profile.Name.Trim();
            profile.SystemPrompt = string.IsNullOrWhiteSpace(profile.SystemPrompt) ? DefaultSystemPrompt : profile.SystemPrompt.Trim();
        }

        if (ApiProfiles.Count == 0)
        {
            SelectedApiProfileId = string.Empty;
            BaseUrl = string.Empty;
            ApiKey = string.Empty;
            Model = string.Empty;
            SystemPrompt = DefaultSystemPrompt;
            EnableApiContext = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedApiProfileId)
            || ApiProfiles.All(profile => !string.Equals(profile.Id, SelectedApiProfileId, StringComparison.Ordinal)))
        {
            SelectedApiProfileId = ApiProfiles[0].Id;
        }

        SyncLegacyApiFieldsFromSelectedProfile();
    }

    public void NormalizeAiProviderProfiles()
    {
        for (var index = 0; index < AiProviderProfiles.Count; index++)
        {
            AiProviderProfiles[index].Normalize(index);
        }

        if (AiProviderProfiles.Count == 0)
        {
            SelectedAiProviderProfileId = string.Empty;
            return;
        }

        var selectedProfile = AiProviderProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, SelectedAiProviderProfileId, StringComparison.Ordinal));
        if (selectedProfile is null || !selectedProfile.IsEnabled)
        {
            SelectedAiProviderProfileId = AiProviderProfiles
                .FirstOrDefault(profile => profile.IsEnabled && profile.Supports(AiModelCapabilities.VisionAnalysis))?.Id
                ?? AiProviderProfiles.FirstOrDefault(profile => profile.IsEnabled)?.Id
                ?? string.Empty;
        }

    }

    public void NormalizeImageGenerationProfiles()
    {
        for (var index = 0; index < ImageGenerationProfiles.Count; index++)
        {
            ImageGenerationProfiles[index].Normalize(index);
        }

        if (ImageGenerationProfiles.Count == 0)
        {
            SelectedImageGenerationProfileId = string.Empty;
            return;
        }

        var selected = ImageGenerationProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, SelectedImageGenerationProfileId, StringComparison.Ordinal));
        if (selected is null || !selected.IsEnabled)
        {
            SelectedImageGenerationProfileId = ImageGenerationProfiles
                .FirstOrDefault(profile => profile.IsEnabled && profile.IsDefault)?.Id
                ?? ImageGenerationProfiles.FirstOrDefault(profile => profile.IsEnabled)?.Id
                ?? string.Empty;
        }

        foreach (var profile in ImageGenerationProfiles)
        {
            profile.IsDefault = string.Equals(profile.Id, SelectedImageGenerationProfileId, StringComparison.Ordinal);
        }
    }

    public ImageGenerationProfile? GetSelectedImageGenerationProfile()
    {
        return ImageGenerationProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, SelectedImageGenerationProfileId, StringComparison.Ordinal))
            ?? ImageGenerationProfiles.FirstOrDefault(profile => profile.IsEnabled)
            ?? ImageGenerationProfiles.FirstOrDefault();
    }

    public AiProviderProfile? GetSelectedAiProviderProfile()
    {
        return AiProviderProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, SelectedAiProviderProfileId, StringComparison.Ordinal))
            ?? AiProviderProfiles.FirstOrDefault();
    }

    public IReadOnlyList<AiProviderProfile> GetEnabledAiProviderProfiles(AiModelCapabilities requiredCapabilities)
    {
        return AiProviderProfiles
            .Where(profile => profile.IsEnabled && profile.Supports(requiredCapabilities))
            .ToList();
    }

    public void SyncLegacyApiFieldsFromSelectedProfile()
    {
        var selected = GetSelectedApiProfile();
        BaseUrl = selected?.BaseUrl?.Trim() ?? string.Empty;
        ApiKey = selected?.ApiKey ?? string.Empty;
        Model = selected?.Model?.Trim() ?? string.Empty;
        SystemPrompt = string.IsNullOrWhiteSpace(selected?.SystemPrompt)
            ? DefaultSystemPrompt
            : selected!.SystemPrompt.Trim();
        EnableApiContext = selected?.EnableContext == true;
    }

    public static List<ApiTranslationProfile> CloneApiProfiles(IEnumerable<ApiTranslationProfile>? profiles)
    {
        return profiles?.Select(profile => new ApiTranslationProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            BaseUrl = profile.BaseUrl,
            ApiKey = profile.ApiKey,
            Model = profile.Model,
            SystemPrompt = profile.SystemPrompt,
            EnableContext = profile.EnableContext
        }).ToList() ?? [];
    }
}
