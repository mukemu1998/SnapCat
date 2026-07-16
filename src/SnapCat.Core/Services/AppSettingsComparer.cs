using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

/// <summary>
/// Compares user settings without mutating the live UI-bound settings instance.
/// </summary>
public static class AppSettingsComparer
{
    public static bool AreEquivalent(AppSettings left, AppSettings right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var normalizedLeft = AppSettingsCloneService.Clone(left);
        var normalizedRight = AppSettingsCloneService.Clone(right);
        normalizedLeft.NormalizeApiProfiles();
        normalizedRight.NormalizeApiProfiles();
        normalizedLeft.NormalizeAiProviderProfiles();
        normalizedRight.NormalizeAiProviderProfiles();
        normalizedLeft.NormalizeImageGenerationProfiles();
        normalizedRight.NormalizeImageGenerationProfiles();

        return AreApiProfilesEquivalent(normalizedLeft.ApiProfiles, normalizedRight.ApiProfiles)
            && AreAiProviderProfilesEquivalent(normalizedLeft.AiProviderProfiles, normalizedRight.AiProviderProfiles)
            && AreImageGenerationProfilesEquivalent(normalizedLeft.ImageGenerationProfiles, normalizedRight.ImageGenerationProfiles)
            && Same(normalizedLeft.SelectedApiProfileId, normalizedRight.SelectedApiProfileId)
            && Same(normalizedLeft.SelectedAiProviderProfileId, normalizedRight.SelectedAiProviderProfileId)
            && Same(normalizedLeft.SelectedImageGenerationProfileId, normalizedRight.SelectedImageGenerationProfileId)
            && Same(normalizedLeft.TargetLanguage, normalizedRight.TargetLanguage)
            && Same(normalizedLeft.TranslationProviderPreference, normalizedRight.TranslationProviderPreference)
            && normalizedLeft.EnableApiContext == normalizedRight.EnableApiContext
            && Same(normalizedLeft.OcrEngine, normalizedRight.OcrEngine)
            && Same(normalizedLeft.TesseractExecutablePath, normalizedRight.TesseractExecutablePath, StringComparison.Ordinal)
            && Same(normalizedLeft.TesseractLanguage, normalizedRight.TesseractLanguage)
            && normalizedLeft.Temperature.Equals(normalizedRight.Temperature)
            && HotkeyEquals(normalizedLeft.HotkeyCaptureAndPin, normalizedRight.HotkeyCaptureAndPin)
            && HotkeyEquals(normalizedLeft.HotkeyCaptureAndOcr, normalizedRight.HotkeyCaptureAndOcr)
            && HotkeyEquals(normalizedLeft.HotkeyCaptureAndTranslate, normalizedRight.HotkeyCaptureAndTranslate)
            && HotkeyEquals(normalizedLeft.HotkeyCaptureAndWaitForAction, normalizedRight.HotkeyCaptureAndWaitForAction)
            && HotkeyEquals(normalizedLeft.HotkeyCaptureAndSave, normalizedRight.HotkeyCaptureAndSave)
            && HotkeyEquals(normalizedLeft.HotkeyCaptureAndCopy, normalizedRight.HotkeyCaptureAndCopy)
            && HotkeyEquals(normalizedLeft.HotkeyCaptureAndAnnotate, normalizedRight.HotkeyCaptureAndAnnotate)
            && HotkeyEquals(normalizedLeft.HotkeyCaptureAndVisualPrompt, normalizedRight.HotkeyCaptureAndVisualPrompt)
            && HotkeyEquals(normalizedLeft.HotkeyFullScreenCanvasEdit, normalizedRight.HotkeyFullScreenCanvasEdit)
            && HotkeyEquals(normalizedLeft.PinnedCloseShortcut, normalizedRight.PinnedCloseShortcut)
            && HotkeyEquals(normalizedLeft.PinnedHideShortcut, normalizedRight.PinnedHideShortcut)
            && HotkeyEquals(normalizedLeft.HotkeyShowAllPinned, normalizedRight.HotkeyShowAllPinned)
            && HotkeyEquals(normalizedLeft.HotkeyHideAllPinned, normalizedRight.HotkeyHideAllPinned)
            && HotkeyEquals(normalizedLeft.HotkeyShowUngroupedPinned, normalizedRight.HotkeyShowUngroupedPinned)
            && HotkeyEquals(normalizedLeft.HotkeyShowPinnedGroupOne, normalizedRight.HotkeyShowPinnedGroupOne)
            && HotkeyEquals(normalizedLeft.HotkeyShowPinnedGroupTwo, normalizedRight.HotkeyShowPinnedGroupTwo)
            && HotkeyEquals(normalizedLeft.HotkeyShowPinnedGroupThree, normalizedRight.HotkeyShowPinnedGroupThree)
            && HotkeyEquals(normalizedLeft.HotkeyShowMainWindow, normalizedRight.HotkeyShowMainWindow)
            && HotkeyEquals(normalizedLeft.HotkeyExitApplication, normalizedRight.HotkeyExitApplication)
            && Same(normalizedLeft.TrayLeftClickAction, normalizedRight.TrayLeftClickAction)
            && Same(normalizedLeft.TrayTooltipWorkflowOne, normalizedRight.TrayTooltipWorkflowOne)
            && Same(normalizedLeft.TrayTooltipWorkflowTwo, normalizedRight.TrayTooltipWorkflowTwo)
            && Same(CaptureStartupMode.Normalize(normalizedLeft.CaptureStartupMode), CaptureStartupMode.Normalize(normalizedRight.CaptureStartupMode))
            && Same(normalizedLeft.ThemeId, normalizedRight.ThemeId)
            && normalizedLeft.TempFileRetentionDays == normalizedRight.TempFileRetentionDays
            && normalizedLeft.HistoryRetentionDays == normalizedRight.HistoryRetentionDays
            && normalizedLeft.LaunchAtStartup == normalizedRight.LaunchAtStartup
            && normalizedLeft.AutoCheckUpdates == normalizedRight.AutoCheckUpdates;
    }

    private static bool HotkeyEquals(string left, string right) =>
        Same(HotkeyTextNormalizer.Normalize(left), HotkeyTextNormalizer.Normalize(right));

    private static bool Same(string? left, string? right, StringComparison comparison = StringComparison.OrdinalIgnoreCase) =>
        string.Equals(left?.Trim(), right?.Trim(), comparison);

    private static bool AreApiProfilesEquivalent(IReadOnlyList<ApiTranslationProfile> left, IReadOnlyList<ApiTranslationProfile> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftProfile = left[index];
            var rightProfile = right[index];
            if (!Same(leftProfile.Id, rightProfile.Id, StringComparison.Ordinal)
                || !Same(leftProfile.Name, rightProfile.Name, StringComparison.Ordinal)
                || !Same(leftProfile.BaseUrl, rightProfile.BaseUrl, StringComparison.Ordinal)
                || !Same(leftProfile.ApiKey, rightProfile.ApiKey, StringComparison.Ordinal)
                || !Same(leftProfile.Model, rightProfile.Model, StringComparison.Ordinal)
                || !Same(leftProfile.SystemPrompt, rightProfile.SystemPrompt, StringComparison.Ordinal)
                || leftProfile.EnableContext != rightProfile.EnableContext)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreAiProviderProfilesEquivalent(IReadOnlyList<AiProviderProfile> left, IReadOnlyList<AiProviderProfile> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftProfile = left[index];
            var rightProfile = right[index];
            if (!Same(leftProfile.Id, rightProfile.Id, StringComparison.Ordinal)
                || !Same(leftProfile.Name, rightProfile.Name, StringComparison.Ordinal)
                || !Same(leftProfile.Protocol, rightProfile.Protocol)
                || !Same(leftProfile.BaseUrl, rightProfile.BaseUrl, StringComparison.Ordinal)
                || !Same(leftProfile.ApiKey, rightProfile.ApiKey, StringComparison.Ordinal)
                || !Same(leftProfile.Model, rightProfile.Model, StringComparison.Ordinal)
                || leftProfile.IsEnabled != rightProfile.IsEnabled
                || leftProfile.Capabilities != rightProfile.Capabilities
                || leftProfile.MaxReferenceImageCount != rightProfile.MaxReferenceImageCount
                || leftProfile.MaxOutputCount != rightProfile.MaxOutputCount
                || leftProfile.SupportsCostEstimate != rightProfile.SupportsCostEstimate)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreImageGenerationProfilesEquivalent(IReadOnlyList<ImageGenerationProfile> left, IReadOnlyList<ImageGenerationProfile> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftProfile = left[index];
            var rightProfile = right[index];
            if (!Same(leftProfile.Id, rightProfile.Id, StringComparison.Ordinal)
                || !Same(leftProfile.Name, rightProfile.Name, StringComparison.Ordinal)
                || !Same(leftProfile.Protocol, rightProfile.Protocol)
                || !Same(leftProfile.BaseUrl, rightProfile.BaseUrl, StringComparison.Ordinal)
                || !Same(leftProfile.ApiKey, rightProfile.ApiKey, StringComparison.Ordinal)
                || !Same(leftProfile.DefaultCheckpoint, rightProfile.DefaultCheckpoint, StringComparison.Ordinal)
                || leftProfile.IsEnabled != rightProfile.IsEnabled
                || leftProfile.IsDefault != rightProfile.IsDefault
                || leftProfile.DefaultWidth != rightProfile.DefaultWidth
                || leftProfile.DefaultHeight != rightProfile.DefaultHeight
                || leftProfile.DefaultSteps != rightProfile.DefaultSteps
                || !leftProfile.DefaultCfgScale.Equals(rightProfile.DefaultCfgScale))
            {
                return false;
            }
        }

        return true;
    }
}
