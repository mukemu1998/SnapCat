using System.Windows;
using SnapCat.App.Services;
using SnapCat.Core.Models;
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
            TargetLanguage = GetSelectedTargetLanguage(),
            TranslationProviderPreference = GetSelectedTranslationProvider(),
            TesseractExecutablePath = TesseractPathTextBox.Text.Trim(),
            TesseractLanguage = GetSelectedTesseractLanguage(),
            OcrEngine = GetSelectedOcrEngine(),
            HotkeyCaptureAndPin = HotkeyCaptureAndPinTextBox.Text.Trim(),
            HotkeyCaptureAndTranslate = HotkeyCaptureAndTranslateTextBox.Text.Trim(),
            HotkeyCaptureAndWaitForAction = HotkeyCaptureAndWaitTextBox.Text.Trim(),
            HotkeyCaptureAndSave = HotkeyCaptureAndSaveTextBox.Text.Trim(),
            PinnedCloseShortcut = PinnedCloseShortcutTextBox.Text.Trim(),
            PinnedHideShortcut = PinnedHideShortcutTextBox.Text.Trim(),
            HotkeyShowAllPinned = HotkeyShowAllPinnedTextBox.Text.Trim(),
            HotkeyHideAllPinned = HotkeyHideAllPinnedTextBox.Text.Trim(),
            HotkeyShowUngroupedPinned = HotkeyShowUngroupedPinnedTextBox.Text.Trim(),
            HotkeyShowPinnedGroupOne = HotkeyShowPinnedGroupOneTextBox.Text.Trim(),
            HotkeyShowPinnedGroupTwo = HotkeyShowPinnedGroupTwoTextBox.Text.Trim(),
            HotkeyShowPinnedGroupThree = HotkeyShowPinnedGroupThreeTextBox.Text.Trim(),
            HotkeyShowMainWindow = HotkeyShowMainWindowTextBox.Text.Trim(),
            HotkeyExitApplication = HotkeyExitApplicationTextBox.Text.Trim(),
            TrayLeftClickAction = GetSelectedTrayLeftClickAction(),
            ThemeId = GetSelectedThemeId(),
            TempFileRetentionDays = SettingsValueParser.ParseRetentionDays(TempRetentionDaysTextBox.Text, defaults.TempFileRetentionDays),
            HistoryRetentionDays = SettingsValueParser.ParseRetentionDays(HistoryRetentionDaysTextBox.Text, defaults.HistoryRetentionDays),
            LaunchAtStartup = LaunchAtStartupCheckBox.IsChecked == true,
            Temperature = _settings.Temperature
        };

        settings.NormalizeApiProfiles();
        return settings;
    }

    private void ApplySettingsToControls(AppSettings settings)
    {
        _isApplyingSettings = true;
        _suppressTranslationProviderEvents = true;
        _translationProviderSelectionTouched = false;
        settings.NormalizeApiProfiles();
        ApiProfilesEditor.LoadFromSettings(settings);
        ApplyApiProfileState();
        SetTargetLanguageSelection(settings.TargetLanguage);
        TesseractPathTextBox.Text = settings.TesseractExecutablePath;
        SetTesseractLanguageSelection(settings.TesseractLanguage);
        HotkeyCaptureAndPinTextBox.Text = settings.HotkeyCaptureAndPin;
        HotkeyCaptureAndTranslateTextBox.Text = settings.HotkeyCaptureAndTranslate;
        HotkeyCaptureAndWaitTextBox.Text = settings.HotkeyCaptureAndWaitForAction;
        HotkeyCaptureAndSaveTextBox.Text = settings.HotkeyCaptureAndSave;
        PinnedCloseShortcutTextBox.Text = settings.PinnedCloseShortcut;
        PinnedHideShortcutTextBox.Text = settings.PinnedHideShortcut;
        HotkeyShowAllPinnedTextBox.Text = settings.HotkeyShowAllPinned;
        HotkeyHideAllPinnedTextBox.Text = settings.HotkeyHideAllPinned;
        HotkeyShowUngroupedPinnedTextBox.Text = settings.HotkeyShowUngroupedPinned;
        HotkeyShowPinnedGroupOneTextBox.Text = settings.HotkeyShowPinnedGroupOne;
        HotkeyShowPinnedGroupTwoTextBox.Text = settings.HotkeyShowPinnedGroupTwo;
        HotkeyShowPinnedGroupThreeTextBox.Text = settings.HotkeyShowPinnedGroupThree;
        HotkeyShowMainWindowTextBox.Text = settings.HotkeyShowMainWindow;
        HotkeyExitApplicationTextBox.Text = settings.HotkeyExitApplication;
        TempRetentionDaysTextBox.Text = settings.TempFileRetentionDays.ToString();
        HistoryRetentionDaysTextBox.Text = settings.HistoryRetentionDays.ToString();
        LaunchAtStartupCheckBox.IsChecked = settings.LaunchAtStartup;
        SetOcrEngineSelection(settings.OcrEngine);
        SetTrayLeftClickSelection(settings.TrayLeftClickAction);
        SetThemeSelection(settings.ThemeId);
        SetTranslationProviderSelection(string.IsNullOrWhiteSpace(settings.TranslationProviderPreference)
            ? (SmartTranslationService.HasCustomApiSettings(settings)
                ? TranslationProviderPreference.Api
                : TranslationProviderPreference.Local)
            : settings.TranslationProviderPreference);
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
        if (!ComboBoxSelectionHelper.SelectByTag(OcrEngineComboBox, value, StringComparison.Ordinal))
        {
            OcrEngineComboBox.SelectedIndex = 0;
        }
    }

    private string GetSelectedOcrEngine()
    {
        return ComboBoxSelectionHelper.GetSelectedTag(OcrEngineComboBox, "windows-media-ocr");
    }

    private void SetTrayLeftClickSelection(string value)
    {
        if (!ComboBoxSelectionHelper.SelectByTag(TrayLeftClickActionComboBox, value, StringComparison.Ordinal))
        {
            TrayLeftClickActionComboBox.SelectedIndex = 2;
        }
    }

    private string GetSelectedTrayLeftClickAction()
    {
        return ComboBoxSelectionHelper.GetSelectedTag(
            TrayLeftClickActionComboBox,
            nameof(CaptureWorkflowKind.CaptureAndWaitForAction));
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
        MarkSettingsDirty();
    }

    private void MarkSettingsDirty()
    {
        if (!_hasLoadedSettings || _isApplyingSettings)
        {
            return;
        }

        _hasUnsavedSettings = true;
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
