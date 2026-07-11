using SnapCat.Core.Models;

namespace SnapCat.App.Services;

internal static class SettingsComparer
{
    public static bool AreEquivalent(AppSettings left, AppSettings right)
    {
        left.NormalizeApiProfiles();
        right.NormalizeApiProfiles();
        left.NormalizeAiProviderProfiles();
        right.NormalizeAiProviderProfiles();

        return AreApiProfilesEquivalent(left.ApiProfiles, right.ApiProfiles)
            && AreAiProviderProfilesEquivalent(left.AiProviderProfiles, right.AiProviderProfiles)
            && string.Equals(left.SelectedApiProfileId, right.SelectedApiProfileId, StringComparison.Ordinal)
            && string.Equals(left.SelectedAiProviderProfileId, right.SelectedAiProviderProfileId, StringComparison.Ordinal)
            && string.Equals(left.TargetLanguage, right.TargetLanguage, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.TranslationProviderPreference, right.TranslationProviderPreference, StringComparison.OrdinalIgnoreCase)
            && left.EnableApiContext == right.EnableApiContext
            && string.Equals(left.OcrEngine, right.OcrEngine, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.TesseractExecutablePath, right.TesseractExecutablePath, StringComparison.Ordinal)
            && string.Equals(left.TesseractLanguage, right.TesseractLanguage, StringComparison.OrdinalIgnoreCase)
            && HotkeyEquals(left.HotkeyCaptureAndPin, right.HotkeyCaptureAndPin)
            && HotkeyEquals(left.HotkeyCaptureAndOcr, right.HotkeyCaptureAndOcr)
            && HotkeyEquals(left.HotkeyCaptureAndTranslate, right.HotkeyCaptureAndTranslate)
            && HotkeyEquals(left.HotkeyCaptureAndWaitForAction, right.HotkeyCaptureAndWaitForAction)
            && HotkeyEquals(left.HotkeyCaptureAndSave, right.HotkeyCaptureAndSave)
            && HotkeyEquals(left.HotkeyCaptureAndCopy, right.HotkeyCaptureAndCopy)
            && HotkeyEquals(left.HotkeyCaptureAndAnnotate, right.HotkeyCaptureAndAnnotate)
            && HotkeyEquals(left.HotkeyCaptureAndVisualPrompt, right.HotkeyCaptureAndVisualPrompt)
            && HotkeyEquals(left.HotkeyFullScreenCanvasEdit, right.HotkeyFullScreenCanvasEdit)
            && HotkeyEquals(left.PinnedCloseShortcut, right.PinnedCloseShortcut)
            && HotkeyEquals(left.PinnedHideShortcut, right.PinnedHideShortcut)
            && HotkeyEquals(left.HotkeyShowAllPinned, right.HotkeyShowAllPinned)
            && HotkeyEquals(left.HotkeyHideAllPinned, right.HotkeyHideAllPinned)
            && HotkeyEquals(left.HotkeyShowUngroupedPinned, right.HotkeyShowUngroupedPinned)
            && HotkeyEquals(left.HotkeyShowPinnedGroupOne, right.HotkeyShowPinnedGroupOne)
            && HotkeyEquals(left.HotkeyShowPinnedGroupTwo, right.HotkeyShowPinnedGroupTwo)
            && HotkeyEquals(left.HotkeyShowPinnedGroupThree, right.HotkeyShowPinnedGroupThree)
            && HotkeyEquals(left.HotkeyShowMainWindow, right.HotkeyShowMainWindow)
            && HotkeyEquals(left.HotkeyExitApplication, right.HotkeyExitApplication)
            && string.Equals(left.TrayLeftClickAction, right.TrayLeftClickAction, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.TrayTooltipWorkflowOne, right.TrayTooltipWorkflowOne, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.TrayTooltipWorkflowTwo, right.TrayTooltipWorkflowTwo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(CaptureStartupMode.Normalize(left.CaptureStartupMode), CaptureStartupMode.Normalize(right.CaptureStartupMode), StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ThemeId, right.ThemeId, StringComparison.OrdinalIgnoreCase)
            && left.TempFileRetentionDays == right.TempFileRetentionDays
            && left.HistoryRetentionDays == right.HistoryRetentionDays
            && left.LaunchAtStartup == right.LaunchAtStartup;
    }

    private static bool HotkeyEquals(string left, string right)
    {
        return string.Equals(
            HotkeyTextFormatter.FormatText(left),
            HotkeyTextFormatter.FormatText(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreApiProfilesEquivalent(
        IReadOnlyList<ApiTranslationProfile> left,
        IReadOnlyList<ApiTranslationProfile> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftProfile = left[index];
            var rightProfile = right[index];
            if (!string.Equals(leftProfile.Id, rightProfile.Id, StringComparison.Ordinal)
                || !string.Equals(leftProfile.Name, rightProfile.Name, StringComparison.Ordinal)
                || !string.Equals(leftProfile.BaseUrl, rightProfile.BaseUrl, StringComparison.Ordinal)
                || !string.Equals(leftProfile.ApiKey, rightProfile.ApiKey, StringComparison.Ordinal)
                || !string.Equals(leftProfile.Model, rightProfile.Model, StringComparison.Ordinal)
                || !string.Equals(leftProfile.SystemPrompt, rightProfile.SystemPrompt, StringComparison.Ordinal)
                || leftProfile.EnableContext != rightProfile.EnableContext)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreAiProviderProfilesEquivalent(
        IReadOnlyList<AiProviderProfile> left,
        IReadOnlyList<AiProviderProfile> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftProfile = left[index];
            var rightProfile = right[index];
            if (!string.Equals(leftProfile.Id, rightProfile.Id, StringComparison.Ordinal)
                || !string.Equals(leftProfile.Name, rightProfile.Name, StringComparison.Ordinal)
                || !string.Equals(leftProfile.Protocol, rightProfile.Protocol, StringComparison.Ordinal)
                || !string.Equals(leftProfile.BaseUrl, rightProfile.BaseUrl, StringComparison.Ordinal)
                || !string.Equals(leftProfile.ApiKey, rightProfile.ApiKey, StringComparison.Ordinal)
                || !string.Equals(leftProfile.Model, rightProfile.Model, StringComparison.Ordinal)
                || leftProfile.IsEnabled != rightProfile.IsEnabled
                || leftProfile.Capabilities != rightProfile.Capabilities)
            {
                return false;
            }
        }

        return true;
    }
}
