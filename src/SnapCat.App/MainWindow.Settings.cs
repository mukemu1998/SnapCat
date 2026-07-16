using System.Windows;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using SnapCat.Core.Services;
using SnapCat.Infrastructure.Services;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace SnapCat.App;

public partial class MainWindow
{
    private async Task<bool> TryApplySettingsAsync(AppSettings settings)
    {
        try
        {
            _app.StartupRegistrationService.SetEnabled(settings.LaunchAtStartup);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                this,
                $"开机自启设置失败：{ex.Message}",
                "保存设置失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        _settings = settings;
        _settings.ThemeId = _app.ThemeService.ApplyTheme(WpfApplication.Current, settings.ThemeId);
        _app.TrayIconService.RefreshThemeIcon();
        RefreshWindowThemeIcon();
        _settings.LaunchAtStartup = _app.StartupRegistrationService.IsEnabled();

        await _app.SettingsStore.SaveAsync(_settings);
        RegisterHotkeys();
        ApplySettingsToControls(_settings);
        ValidateHotkeyConflicts();
        RenderSettingsSummary();
        RenderEnvironmentChecks();
        MarkSettingsClean();
        return true;
    }

    private AppSettings BuildCurrentSettings()
    {
        PersistCurrentApiProfileEditor();
        var defaults = new AppSettings();

        var settings = new AppSettings
        {
            ApiProfiles = ApiProfilesEditor.ToModels(),
            SelectedApiProfileId = ApiProfilesEditor.SelectedProfileId,
            AiProviderProfiles = AiProviderProfile.CloneAll(_visualPromptProfiles),
            SelectedAiProviderProfileId = _defaultVisualPromptProfileId,
            ImageGenerationProfiles = ImageGenerationProfile.CloneAll(_imageGenerationProfiles),
            SelectedImageGenerationProfileId = _selectedImageGenerationProfileId,
            TargetLanguage = GetSelectedTargetLanguage(),
            TranslationProviderPreference = GetSelectedTranslationProvider(),
            TesseractExecutablePath = TesseractPathTextBox.Text.Trim(),
            TesseractLanguage = GetSelectedTesseractLanguage(),
            OcrEngine = GetSelectedOcrEngine(),
            HotkeyCaptureAndPin = FormatHotkeySetting(HotkeyCaptureAndPinTextBox.Text),
            HotkeyCaptureAndOcr = FormatHotkeySetting(HotkeyCaptureAndOcrTextBox.Text),
            HotkeyCaptureAndTranslate = FormatHotkeySetting(HotkeyCaptureAndTranslateTextBox.Text),
            HotkeyCaptureAndWaitForAction = FormatHotkeySetting(HotkeyCaptureAndWaitTextBox.Text),
            HotkeyCaptureAndSave = FormatHotkeySetting(HotkeyCaptureAndSaveTextBox.Text),
            HotkeyCaptureAndCopy = FormatHotkeySetting(HotkeyCaptureAndCopyTextBox.Text),
            HotkeyCaptureAndAnnotate = FormatHotkeySetting(HotkeyCaptureAndAnnotateTextBox.Text),
            HotkeyCaptureAndVisualPrompt = FormatHotkeySetting(HotkeyCaptureAndVisualPromptTextBox.Text),
            HotkeyFullScreenCanvasEdit = FormatHotkeySetting(HotkeyFullScreenCanvasTextBox.Text),
            PinnedCloseShortcut = FormatHotkeySetting(PinnedCloseShortcutTextBox.Text),
            PinnedHideShortcut = FormatHotkeySetting(PinnedHideShortcutTextBox.Text),
            HotkeyShowAllPinned = FormatHotkeySetting(HotkeyShowAllPinnedTextBox.Text),
            HotkeyHideAllPinned = FormatHotkeySetting(HotkeyHideAllPinnedTextBox.Text),
            HotkeyShowUngroupedPinned = FormatHotkeySetting(HotkeyShowUngroupedPinnedTextBox.Text),
            HotkeyShowPinnedGroupOne = FormatHotkeySetting(HotkeyShowPinnedGroupOneTextBox.Text),
            HotkeyShowPinnedGroupTwo = FormatHotkeySetting(HotkeyShowPinnedGroupTwoTextBox.Text),
            HotkeyShowPinnedGroupThree = FormatHotkeySetting(HotkeyShowPinnedGroupThreeTextBox.Text),
            HotkeyShowMainWindow = FormatHotkeySetting(HotkeyShowMainWindowTextBox.Text),
            HotkeyExitApplication = FormatHotkeySetting(HotkeyExitApplicationTextBox.Text),
            CaptureStartupMode = GetSelectedCaptureStartupMode(),
            TrayLeftClickAction = GetSelectedTrayLeftClickAction(),
            TrayTooltipWorkflowOne = GetSelectedTrayTooltipWorkflowOne(),
            TrayTooltipWorkflowTwo = GetSelectedTrayTooltipWorkflowTwo(),
            ThemeId = GetSelectedThemeId(),
            TempFileRetentionDays = SettingsValueParser.ParseRetentionDays(TempRetentionDaysTextBox.Text, defaults.TempFileRetentionDays),
            HistoryRetentionDays = SettingsValueParser.ParseRetentionDays(HistoryRetentionDaysTextBox.Text, defaults.HistoryRetentionDays),
            LaunchAtStartup = LaunchAtStartupCheckBox.IsChecked == true,
            AutoCheckUpdates = AutoCheckUpdatesCheckBox.IsChecked == true,
            Temperature = _settings.Temperature
        };

        settings.NormalizeApiProfiles();
        settings.NormalizeAiProviderProfiles();
        settings.NormalizeImageGenerationProfiles();
        return settings;
    }

    private void ApplySettingsToControls(AppSettings settings)
    {
        _isApplyingSettings = true;
        _suppressTranslationProviderEvents = true;
        _translationProviderSelectionTouched = false;
        settings.NormalizeApiProfiles();
        settings.NormalizeImageGenerationProfiles();
        ApiProfilesEditor.LoadFromSettings(settings);
        ApplyApiProfileState();
        SetTargetLanguageSelection(settings.TargetLanguage);
        TesseractPathTextBox.Text = settings.TesseractExecutablePath;
        SetTesseractLanguageSelection(settings.TesseractLanguage);
        HotkeyCaptureAndPinTextBox.Text = FormatHotkeySetting(settings.HotkeyCaptureAndPin);
        HotkeyCaptureAndOcrTextBox.Text = FormatHotkeySetting(settings.HotkeyCaptureAndOcr);
        HotkeyCaptureAndTranslateTextBox.Text = FormatHotkeySetting(settings.HotkeyCaptureAndTranslate);
        HotkeyCaptureAndWaitTextBox.Text = FormatHotkeySetting(settings.HotkeyCaptureAndWaitForAction);
        HotkeyCaptureAndSaveTextBox.Text = FormatHotkeySetting(settings.HotkeyCaptureAndSave);
        HotkeyCaptureAndCopyTextBox.Text = FormatHotkeySetting(settings.HotkeyCaptureAndCopy);
        HotkeyCaptureAndAnnotateTextBox.Text = FormatHotkeySetting(settings.HotkeyCaptureAndAnnotate);
        HotkeyCaptureAndVisualPromptTextBox.Text = FormatHotkeySetting(settings.HotkeyCaptureAndVisualPrompt);
        HotkeyFullScreenCanvasTextBox.Text = FormatHotkeySetting(settings.HotkeyFullScreenCanvasEdit);
        PinnedCloseShortcutTextBox.Text = FormatHotkeySetting(settings.PinnedCloseShortcut);
        PinnedHideShortcutTextBox.Text = FormatHotkeySetting(settings.PinnedHideShortcut);
        HotkeyShowAllPinnedTextBox.Text = FormatHotkeySetting(settings.HotkeyShowAllPinned);
        HotkeyHideAllPinnedTextBox.Text = FormatHotkeySetting(settings.HotkeyHideAllPinned);
        HotkeyShowUngroupedPinnedTextBox.Text = FormatHotkeySetting(settings.HotkeyShowUngroupedPinned);
        HotkeyShowPinnedGroupOneTextBox.Text = FormatHotkeySetting(settings.HotkeyShowPinnedGroupOne);
        HotkeyShowPinnedGroupTwoTextBox.Text = FormatHotkeySetting(settings.HotkeyShowPinnedGroupTwo);
        HotkeyShowPinnedGroupThreeTextBox.Text = FormatHotkeySetting(settings.HotkeyShowPinnedGroupThree);
        HotkeyShowMainWindowTextBox.Text = FormatHotkeySetting(settings.HotkeyShowMainWindow);
        HotkeyExitApplicationTextBox.Text = FormatHotkeySetting(settings.HotkeyExitApplication);
        TempRetentionDaysTextBox.Text = settings.TempFileRetentionDays.ToString();
        HistoryRetentionDaysTextBox.Text = settings.HistoryRetentionDays.ToString();
        LaunchAtStartupCheckBox.IsChecked = settings.LaunchAtStartup;
        AutoCheckUpdatesCheckBox.IsChecked = settings.AutoCheckUpdates;
        SetOcrEngineSelection(settings.OcrEngine);
        SetCaptureStartupModeSelection(settings.CaptureStartupMode);
        SetTrayLeftClickSelection(settings.TrayLeftClickAction);
        SetTrayTooltipWorkflowSelection(TrayTooltipWorkflowOneComboBox, settings.TrayTooltipWorkflowOne, nameof(CaptureWorkflowKind.CaptureAndTranslate));
        SetTrayTooltipWorkflowSelection(TrayTooltipWorkflowTwoComboBox, settings.TrayTooltipWorkflowTwo, nameof(CaptureWorkflowKind.CaptureAndPin));
        SetThemeSelection(settings.ThemeId);
        SetTranslationProviderSelection(string.IsNullOrWhiteSpace(settings.TranslationProviderPreference)
            ? (SmartTranslationService.HasCustomApiSettings(settings)
                ? TranslationProviderPreference.Api
                : TranslationProviderPreference.Local)
            : settings.TranslationProviderPreference);
        LoadVisualPromptProfiles(settings);
        LoadImageGenerationProfiles(settings);
        _suppressTranslationProviderEvents = false;
        _isApplyingSettings = false;
        MarkSettingsClean();
    }

    private void SettingsInput_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        MarkSettingsDirty();
    }

    private void SettingsSelection_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        MarkSettingsDirty();
    }

    private void SettingsToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        MarkSettingsDirty();
    }

    private void RenderSettingsSummary()
    {
        SettingsSummaryTextBlock.Text = SettingsSummaryFormatter.BuildSettingsSummary(_settings, _app.UserDataDirectory);
    }

    private void RenderUserConfigLocationInfo()
    {
        if (UserConfigDirectoryTextBlock is null || UserConfigLocationStatusTextBlock is null)
        {
            return;
        }

        var nextStartupDirectory = _app.UserDataLocationService.ResolveUserDataDirectory();
        var isPendingSwitch = !string.Equals(
            _app.UserDataDirectory,
            nextStartupDirectory,
            StringComparison.OrdinalIgnoreCase);

        UserConfigDirectoryTextBlock.Text = isPendingSwitch
            ? $"当前运行：{_app.UserDataDirectory}\n下次启动：{nextStartupDirectory}"
            : _app.UserDataDirectory;
        UserConfigLocationStatusTextBlock.Text = _app.UserDataLocationService.IsUsingDefaultDirectory(nextStartupDirectory)
            ? "当前使用默认用户配置目录。"
            : $"当前使用自定义目录，重启后完全生效。位置索引文件：{_app.UserDataLocationService.LocationFilePath}";
    }

    private void RenderEnvironmentChecks()
    {
        var warnings = _app.StartupDiagnosticsService.BuildWarnings(_settings);
        if (warnings.Count == 0)
        {
            EnvironmentCheckTitleTextBlock.Text = "启动检查";
            EnvironmentCheckTitleTextBlock.Foreground = GetThemeBrush("Theme.Brush.Highlight");
            EnvironmentCheckTextBlock.Foreground = GetThemeBrush("Theme.Brush.TextSecondary");
            EnvironmentCheckTextBlock.Text = "环境检查通过，OCR、翻译配置和快捷键格式看起来都正常。";
            return;
        }

        EnvironmentCheckTitleTextBlock.Text = $"启动检查：发现 {warnings.Count} 个需要处理的问题";
        EnvironmentCheckTitleTextBlock.Foreground = GetThemeBrush("Theme.Brush.HighlightAlt");
        EnvironmentCheckTextBlock.Foreground = GetThemeBrush("Theme.Brush.TextSecondary");
        EnvironmentCheckTextBlock.Text = string.Join(Environment.NewLine, warnings.Select(static warning => $"• {warning}"));
    }

    private void SetTestButtonsEnabled(bool isEnabled)
    {
        TestOcrButton.IsEnabled = isEnabled;
        TestApiConnectionButton.IsEnabled = isEnabled;
        TestTranslationButton.IsEnabled = isEnabled;
    }

    private void SetOcrEngineSelection(string value)
    {
        if (!ComboBoxSelectionHelper.SelectByTag(OcrEngineComboBox, NormalizeOcrEngine(value), StringComparison.Ordinal))
        {
            OcrEngineComboBox.SelectedIndex = 0;
        }
    }

    private string GetSelectedOcrEngine()
    {
        return NormalizeOcrEngine(ComboBoxSelectionHelper.GetSelectedTag(OcrEngineComboBox, "windows-text-extractor"));
    }

    private static string NormalizeOcrEngine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "windows-text-extractor";
        }

        return value switch
        {
            // v0.4.3 removed the experimental local vision OCR route after real-device validation.
            // Existing user-local settings safely return to the recommended Windows OCR path.
            "local-ai-ocr" => "windows-text-extractor",
            "windows-snipping-clipboard" => "windows-text-extractor",
            "enhanced-tesseract" => "windows-text-extractor",
            "tesseract-cli" => "windows-text-extractor",
            _ => value
        };
    }

    private void SetTrayLeftClickSelection(string value)
    {
        if (!ComboBoxSelectionHelper.SelectByTag(TrayLeftClickActionComboBox, value, StringComparison.Ordinal))
        {
            TrayLeftClickActionComboBox.SelectedIndex = 3;
        }
    }

    private string GetSelectedTrayLeftClickAction()
    {
        return ComboBoxSelectionHelper.GetSelectedTag(
            TrayLeftClickActionComboBox,
            nameof(CaptureWorkflowKind.CaptureAndWaitForAction));
    }

    private void SetTrayTooltipWorkflowSelection(System.Windows.Controls.ComboBox comboBox, string value, string fallback)
    {
        if (!ComboBoxSelectionHelper.SelectByTag(comboBox, value, StringComparison.Ordinal))
        {
            ComboBoxSelectionHelper.SelectByTag(comboBox, fallback, StringComparison.Ordinal);
        }
    }

    private string GetSelectedTrayTooltipWorkflowOne()
    {
        return ComboBoxSelectionHelper.GetSelectedTag(
            TrayTooltipWorkflowOneComboBox,
            nameof(CaptureWorkflowKind.CaptureAndTranslate));
    }

    private string GetSelectedTrayTooltipWorkflowTwo()
    {
        return ComboBoxSelectionHelper.GetSelectedTag(
            TrayTooltipWorkflowTwoComboBox,
            nameof(CaptureWorkflowKind.CaptureAndPin));
    }

    private static string FormatHotkeySetting(string? value)
    {
        return HotkeyTextFormatter.FormatText(value);
    }

    private void SetCaptureStartupModeSelection(string value)
    {
        if (!ComboBoxSelectionHelper.SelectByTag(
                CaptureStartupModeComboBox,
                CaptureStartupMode.Normalize(value),
                StringComparison.Ordinal))
        {
            CaptureStartupModeComboBox.SelectedIndex = 0;
        }
    }

    private string GetSelectedCaptureStartupMode()
    {
        return CaptureStartupMode.Normalize(ComboBoxSelectionHelper.GetSelectedTag(
            CaptureStartupModeComboBox,
            CaptureStartupMode.Snapshot));
    }

    private void SetThemeSelection(string value)
    {
        var normalized = _app.ThemeService.NormalizeThemeId(value);

        if (!ComboBoxSelectionHelper.SelectByTag(ThemeComboBox, normalized))
        {
            ThemeComboBox.SelectedIndex = 0;
        }
    }

    private string GetSelectedThemeId()
    {
        return ComboBoxSelectionHelper.GetSelectedTag(
            ThemeComboBox,
            _app.ThemeService.NormalizeThemeId(_settings.ThemeId));
    }

    private void ThemeComboBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ThemeComboBox.SelectedItem is null)
        {
            return;
        }

        var selectedThemeId = GetSelectedThemeId();
        _app.ThemeService.ApplyTheme(WpfApplication.Current, selectedThemeId);
        _app.TrayIconService.RefreshThemeIcon();
        RefreshWindowThemeIcon();
        MarkSettingsDirty();
    }

    private void MarkSettingsDirty()
    {
        if (!_hasLoadedSettings || _isApplyingSettings)
        {
            return;
        }

        // Controls can raise change notifications while the window is being arranged or a
        // section is switched. Only expose the save action for a real settings difference.
        _hasUnsavedSettings = !AppSettingsComparer.AreEquivalent(BuildCurrentSettings(), _settings);
        UpdateSaveButtonVisibility();
    }

    private void MarkSettingsClean()
    {
        _hasUnsavedSettings = false;
        UpdateSaveButtonVisibility();
    }

    private void UpdateSaveButtonVisibility()
    {
        if (!_hasLoadedSettings || SaveSettingsButton is null)
        {
            return;
        }

        SaveSettingsButton.Visibility = _hasUnsavedSettings
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SetTranslationProviderSelection(string value)
    {
        if (!ComboBoxSelectionHelper.SelectByTag(TranslationProviderComboBox, value))
        {
            TranslationProviderComboBox.SelectedIndex = 0;
        }
    }

    private string GetSelectedTranslationProvider()
    {
        return ComboBoxSelectionHelper.GetSelectedTag(TranslationProviderComboBox, TranslationProviderPreference.Local);
    }

    private void SetTargetLanguageSelection(string value)
    {
        ComboBoxSelectionHelper.SelectByTagOrCreateCustom(TargetLanguageComboBox, value, "自定义目标语言");
    }

    private string GetSelectedTargetLanguage()
    {
        return ComboBoxSelectionHelper.GetSelectedTag(TargetLanguageComboBox, "zh-CN");
    }

    private void SetTesseractLanguageSelection(string value)
    {
        ComboBoxSelectionHelper.SelectByTagOrCreateCustom(TesseractLanguageComboBox, value, "自定义 OCR 语言");
    }

    private string GetSelectedTesseractLanguage()
    {
        return ComboBoxSelectionHelper.GetSelectedTag(TesseractLanguageComboBox, "chi_sim+eng");
    }
}
