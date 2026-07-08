using SnapCat.Core.Models;

namespace SnapCat.App.Services;

public static class TranslationLanguageHelper
{
    public const string AutoLanguage = "auto";
    public const string ChineseSimplified = "zh-CN";
    public const string English = "en";
    public const string Japanese = "ja";
    public const string Korean = "ko";
    public const string Vietnamese = "vi";
    public const string French = "fr";
    public const string German = "de";
    public const string Russian = "ru";

    public static IReadOnlyList<TranslationLanguageDefinition> SupportedLanguages { get; } =
    [
        new(AutoLanguage, "自动"),
        new(ChineseSimplified, "简体中文"),
        new(English, "英语"),
        new(Japanese, "日语"),
        new(Korean, "韩语"),
        new(Vietnamese, "越南语"),
        new(French, "法语"),
        new(German, "德语"),
        new(Russian, "俄语")
    ];

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

    public static string GetLanguageLabel(string languageCode)
    {
        return SupportedLanguages.FirstOrDefault(language =>
            string.Equals(language.Code, languageCode, StringComparison.OrdinalIgnoreCase))?.Label ?? "自动";
    }

    public static string ResolveSpeechLanguage(string selectedLanguageCode, string? text, string fallbackLanguageCode = English)
    {
        if (!string.IsNullOrWhiteSpace(selectedLanguageCode)
            && !string.Equals(selectedLanguageCode, AutoLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return selectedLanguageCode.Trim();
        }

        var content = text ?? string.Empty;
        if (LooksLikeChinese(content))
        {
            return ChineseSimplified;
        }

        if (content.Any(static character => character is >= '\u3040' and <= '\u30ff'))
        {
            return Japanese;
        }

        if (content.Any(static character => character is >= '\uac00' and <= '\ud7af'))
        {
            return Korean;
        }

        if (content.Any(static character => character is >= '\u0400' and <= '\u04ff'))
        {
            return Russian;
        }

        return string.IsNullOrWhiteSpace(fallbackLanguageCode) ? English : fallbackLanguageCode;
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
        HotkeyCaptureAndOcr = settings.HotkeyCaptureAndOcr,
        HotkeyCaptureAndTranslate = settings.HotkeyCaptureAndTranslate,
        HotkeyCaptureAndWaitForAction = settings.HotkeyCaptureAndWaitForAction,
        HotkeyCaptureAndSave = settings.HotkeyCaptureAndSave,
        HotkeyCaptureAndCopy = settings.HotkeyCaptureAndCopy,
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
        CaptureStartupMode = CaptureStartupMode.Normalize(settings.CaptureStartupMode),
        ThemeId = settings.ThemeId,
        TempFileRetentionDays = settings.TempFileRetentionDays,
        HistoryRetentionDays = settings.HistoryRetentionDays,
        LaunchAtStartup = settings.LaunchAtStartup
    };
}

public sealed record TranslationLanguageDefinition(string Code, string Label);
