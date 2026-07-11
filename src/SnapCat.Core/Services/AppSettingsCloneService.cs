using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

/// <summary>
/// Creates detached settings snapshots for execution and dirty-state comparisons.
/// User-owned profile collections must never be shared between UI sessions.
/// </summary>
public static class AppSettingsCloneService
{
    public static AppSettings Clone(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new AppSettings
        {
            BaseUrl = settings.BaseUrl,
            ApiKey = settings.ApiKey,
            Model = settings.Model,
            SystemPrompt = settings.SystemPrompt,
            EnableApiContext = settings.EnableApiContext,
            ApiProfiles = AppSettings.CloneApiProfiles(settings.ApiProfiles),
            SelectedApiProfileId = settings.SelectedApiProfileId,
            AiProviderProfiles = AiProviderProfile.CloneAll(settings.AiProviderProfiles),
            SelectedAiProviderProfileId = settings.SelectedAiProviderProfileId,
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
            HotkeyCaptureAndAnnotate = settings.HotkeyCaptureAndAnnotate,
            HotkeyCaptureAndVisualPrompt = settings.HotkeyCaptureAndVisualPrompt,
            HotkeyFullScreenCanvasEdit = settings.HotkeyFullScreenCanvasEdit,
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
            TrayTooltipWorkflowOne = settings.TrayTooltipWorkflowOne,
            TrayTooltipWorkflowTwo = settings.TrayTooltipWorkflowTwo,
            CaptureStartupMode = CaptureStartupMode.Normalize(settings.CaptureStartupMode),
            ThemeId = settings.ThemeId,
            TempFileRetentionDays = settings.TempFileRetentionDays,
            HistoryRetentionDays = settings.HistoryRetentionDays,
            LaunchAtStartup = settings.LaunchAtStartup,
            AutoCheckUpdates = settings.AutoCheckUpdates
        };
    }
}
