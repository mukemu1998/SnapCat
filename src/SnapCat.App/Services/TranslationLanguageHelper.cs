using SnapCat.Core.Models;

namespace SnapCat.App.Services;

public static class TranslationLanguageHelper
{
    public const string AutoLanguage = "auto";
    public const string ChineseSimplified = "zh-CN";
    public const string English = "en";
    public const string Japanese = "ja";
    public const string Korean = "ko";

    public static AppSettings BuildSettingsForTranslation(
        AppSettings baseSettings,
        string sourceText,
        string? selectedTargetLanguage = null)
    {
        var settings = CloneSettings(baseSettings);
        settings.NormalizeApiProfiles();
        settings.TargetLanguage = ResolveTargetLanguage(baseSettings, sourceText, selectedTargetLanguage);
        return settings;
    }

    public static string ResolveTargetLanguage(
        AppSettings baseSettings,
        string sourceText,
        string? selectedTargetLanguage = null)
    {
        if (!string.IsNullOrWhiteSpace(selectedTargetLanguage)
            && !string.Equals(selectedTargetLanguage, AutoLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return selectedTargetLanguage.Trim();
        }

        var configuredTarget = baseSettings.TargetLanguage?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredTarget)
            && !string.Equals(configuredTarget, AutoLanguage, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(configuredTarget, ChineseSimplified, StringComparison.OrdinalIgnoreCase))
        {
            return configuredTarget;
        }

        return LooksLikeChinese(sourceText) ? English : ChineseSimplified;
    }

    public static bool LooksLikeChinese(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Any(static character => character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff');
    }

    public static AppSettings CloneSettings(AppSettings settings) => new()
    {
        BaseUrl = settings.BaseUrl,
        ApiKey = settings.ApiKey,
        Model = settings.Model,
        SystemPrompt = settings.SystemPrompt,
        EnableApiContext = settings.EnableApiContext,
        ApiProfiles = AppSettings.CloneApiProfiles(settings.ApiProfiles),
        SelectedApiProfileId = settings.SelectedApiProfileId,
        TargetLanguage = settings.TargetLanguage,
        TranslationProviderPreference = settings.TranslationProviderPreference,
        OcrEngine = settings.OcrEngine,
        TesseractExecutablePath = settings.TesseractExecutablePath,
        TesseractLanguage = settings.TesseractLanguage,
        Temperature = settings.Temperature,
        HotkeyCaptureAndPin = settings.HotkeyCaptureAndPin,
        HotkeyCaptureAndTranslate = settings.HotkeyCaptureAndTranslate,
        HotkeyCaptureAndWaitForAction = settings.HotkeyCaptureAndWaitForAction,
        HotkeyCaptureAndSave = settings.HotkeyCaptureAndSave,
        PinnedCloseShortcut = settings.PinnedCloseShortcut,
        PinnedHideShortcut = settings.PinnedHideShortcut,
        HotkeyShowAllPinned = settings.HotkeyShowAllPinned,
        HotkeyHideAllPinned = settings.HotkeyHideAllPinned,
        HotkeyShowUngroupedPinned = settings.HotkeyShowUngroupedPinned,
        HotkeyShowPinnedGroupOne = settings.HotkeyShowPinnedGroupOne,
        HotkeyShowPinnedGroupTwo = settings.HotkeyShowPinnedGroupTwo,
        HotkeyShowPinnedGroupThree = settings.HotkeyShowPinnedGroupThree,
        HotkeyShowMainWindow = settings.HotkeyShowMainWindow,
        HotkeyExitApplication = settings.HotkeyExitApplication,
        TrayLeftClickAction = settings.TrayLeftClickAction,
        ThemeId = settings.ThemeId,
        TempFileRetentionDays = settings.TempFileRetentionDays,
        HistoryRetentionDays = settings.HistoryRetentionDays,
        LaunchAtStartup = settings.LaunchAtStartup
    };
}
