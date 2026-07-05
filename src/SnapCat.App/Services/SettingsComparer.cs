using SnapCat.Core.Models;

namespace SnapCat.App.Services;

internal static class SettingsComparer
{
    public static bool AreEquivalent(AppSettings left, AppSettings right)
    {
        left.NormalizeApiProfiles();
        right.NormalizeApiProfiles();

        return AreApiProfilesEquivalent(left.ApiProfiles, right.ApiProfiles)
            && string.Equals(left.SelectedApiProfileId, right.SelectedApiProfileId, StringComparison.Ordinal)
            && string.Equals(left.TargetLanguage, right.TargetLanguage, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.TranslationProviderPreference, right.TranslationProviderPreference, StringComparison.OrdinalIgnoreCase)
            && left.EnableApiContext == right.EnableApiContext
            && string.Equals(left.OcrEngine, right.OcrEngine, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.TesseractExecutablePath, right.TesseractExecutablePath, StringComparison.Ordinal)
            && string.Equals(left.TesseractLanguage, right.TesseractLanguage, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyCaptureAndPin, right.HotkeyCaptureAndPin, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyCaptureAndTranslate, right.HotkeyCaptureAndTranslate, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyCaptureAndWaitForAction, right.HotkeyCaptureAndWaitForAction, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyCaptureAndSave, right.HotkeyCaptureAndSave, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.PinnedCloseShortcut, right.PinnedCloseShortcut, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.PinnedHideShortcut, right.PinnedHideShortcut, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyShowAllPinned, right.HotkeyShowAllPinned, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyHideAllPinned, right.HotkeyHideAllPinned, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyShowUngroupedPinned, right.HotkeyShowUngroupedPinned, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyShowPinnedGroupOne, right.HotkeyShowPinnedGroupOne, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyShowPinnedGroupTwo, right.HotkeyShowPinnedGroupTwo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyShowPinnedGroupThree, right.HotkeyShowPinnedGroupThree, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyShowMainWindow, right.HotkeyShowMainWindow, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.HotkeyExitApplication, right.HotkeyExitApplication, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.TrayLeftClickAction, right.TrayLeftClickAction, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ThemeId, right.ThemeId, StringComparison.OrdinalIgnoreCase)
            && left.TempFileRetentionDays == right.TempFileRetentionDays
            && left.HistoryRetentionDays == right.HistoryRetentionDays
            && left.LaunchAtStartup == right.LaunchAtStartup;
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
}
