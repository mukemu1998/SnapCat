using System.Windows;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using SnapCat.Infrastructure.Services;
using WpfApplication = System.Windows.Application;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
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
        UpdateSaveButtonVisibility();
        return true;
    }

    private AppSettings BuildCurrentSettings()
    {
        PersistCurrentApiProfileEditor();

        var settings = new AppSettings
        {
            ApiProfiles = AppSettings.CloneApiProfiles(_editingApiProfiles),
            SelectedApiProfileId = _selectedApiProfileId,
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
            TempFileRetentionDays = ParseRetentionDays(TempRetentionDaysTextBox.Text, new AppSettings().TempFileRetentionDays),
            HistoryRetentionDays = ParseRetentionDays(HistoryRetentionDaysTextBox.Text, new AppSettings().HistoryRetentionDays),
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
        _editingApiProfiles = AppSettings.CloneApiProfiles(settings.ApiProfiles);
        _selectedApiProfileId = settings.SelectedApiProfileId;
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
            ? ResolveDefaultTranslationProvider(settings)
            : settings.TranslationProviderPreference);
        _suppressTranslationProviderEvents = false;
        _isApplyingSettings = false;
        UpdateSaveButtonVisibility();
    }

    private void SettingsInput_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateSaveButtonVisibility();
    }

    private void SettingsSelection_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSaveButtonVisibility();
    }

    private void SettingsToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        UpdateSaveButtonVisibility();
    }

    private static int ParseRetentionDays(string? value, int fallback)
    {
        return int.TryParse(value?.Trim(), out var days)
            ? Math.Max(0, days)
            : Math.Max(0, fallback);
    }

    private void RenderSettingsSummary()
    {
        _settings.NormalizeApiProfiles();
        var selectedProfile = _settings.GetSelectedApiProfile();
        var maskedKey = string.IsNullOrWhiteSpace(selectedProfile?.ApiKey)
            ? "未填写"
            : $"{selectedProfile.ApiKey[..Math.Min(6, selectedProfile.ApiKey.Length)]}...";

        SettingsSummaryTextBlock.Text =
            $"API 配置数：{_settings.ApiProfiles.Count}\n" +
            $"当前 API 配置：{FormatSummaryValue(selectedProfile?.Name ?? string.Empty)}\n" +
            $"接口地址：{FormatSummaryValue(selectedProfile?.BaseUrl ?? string.Empty)}\n" +
            $"模型：{FormatSummaryValue(selectedProfile?.Model ?? string.Empty)}\n" +
            $"上下文翻译：{(selectedProfile?.EnableContext == true ? "已开启" : "未开启")}\n" +
            $"翻译来源：{FormatTranslationProvider(_settings.TranslationProviderPreference)}\n" +
            $"目标语言：{FormatSummaryValue(_settings.TargetLanguage)}\n" +
            $"API Key：{maskedKey}\n" +
            $"OCR：{FormatOcrSummary(_settings)}\n" +
            $"翻译：{FormatTranslationSummary(_settings)}\n" +
            $"开机自启：{(_settings.LaunchAtStartup ? "已开启" : "未开启")}\n" +
            $"快捷键 1（固定到屏幕）：{FormatSummaryValue(_settings.HotkeyCaptureAndPin)}\n" +
            $"快捷键 2（自动翻译）：{FormatSummaryValue(_settings.HotkeyCaptureAndTranslate)}\n" +
            $"快捷键 3（等待操作）：{FormatSummaryValue(_settings.HotkeyCaptureAndWaitForAction)}\n" +
            $"快捷键 4（保存截图）：{FormatSummaryValue(_settings.HotkeyCaptureAndSave)}\n" +
            $"贴图关闭键：{FormatSummaryValue(_settings.PinnedCloseShortcut)}\n" +
            $"贴图隐藏键：{FormatSummaryValue(_settings.PinnedHideShortcut)}\n" +
            $"打开主菜单：{FormatSummaryValue(_settings.HotkeyShowMainWindow)}\n" +
            $"退出软件：{FormatSummaryValue(_settings.HotkeyExitApplication)}\n" +
            $"临时文件保留：{FormatRetentionDays(_settings.TempFileRetentionDays)}\n" +
            $"历史记录保留：{FormatRetentionDays(_settings.HistoryRetentionDays)}\n" +
            $"托盘左键：{FormatTrayLeftClickAction(_settings.TrayLeftClickAction)}\n" +
            $"用户配置目录：{_app.UserDataDirectory}";
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
        foreach (var item in OcrEngineComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), value, StringComparison.Ordinal))
            {
                OcrEngineComboBox.SelectedItem = item;
                return;
            }
        }

        OcrEngineComboBox.SelectedIndex = 0;
    }

    private string GetSelectedOcrEngine()
    {
        return (OcrEngineComboBox.SelectedItem as WpfComboBoxItem)?.Tag?.ToString()
            ?? "windows-media-ocr";
    }

    private void SetTrayLeftClickSelection(string value)
    {
        foreach (var item in TrayLeftClickActionComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), value, StringComparison.Ordinal))
            {
                TrayLeftClickActionComboBox.SelectedItem = item;
                return;
            }
        }

        TrayLeftClickActionComboBox.SelectedIndex = 2;
    }

    private string GetSelectedTrayLeftClickAction()
    {
        return (TrayLeftClickActionComboBox.SelectedItem as WpfComboBoxItem)?.Tag?.ToString()
            ?? nameof(CaptureWorkflowKind.CaptureAndWaitForAction);
    }

    private void SetThemeSelection(string value)
    {
        var normalized = _app.ThemeService.NormalizeThemeId(value);

        foreach (var item in ThemeComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                ThemeComboBox.SelectedItem = item;
                return;
            }
        }

        ThemeComboBox.SelectedIndex = 0;
    }

    private string GetSelectedThemeId()
    {
        return (ThemeComboBox.SelectedItem as WpfComboBoxItem)?.Tag?.ToString()
            ?? _app.ThemeService.NormalizeThemeId(_settings.ThemeId);
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
        UpdateSaveButtonVisibility();
    }

    private void UpdateSaveButtonVisibility()
    {
        if (!_hasLoadedSettings || _isApplyingSettings || SaveSettingsButton is null)
        {
            return;
        }

        SaveSettingsButton.Visibility = AreSettingsEquivalent(BuildCurrentSettings(), _settings)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static bool AreSettingsEquivalent(AppSettings left, AppSettings right)
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

    private void SetTranslationProviderSelection(string value)
    {
        foreach (var item in TranslationProviderComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                TranslationProviderComboBox.SelectedItem = item;
                return;
            }
        }

        TranslationProviderComboBox.SelectedIndex = 0;
    }

    private string GetSelectedTranslationProvider()
    {
        return (TranslationProviderComboBox.SelectedItem as WpfComboBoxItem)?.Tag?.ToString()
            ?? TranslationProviderPreference.Local;
    }

    private void SetTargetLanguageSelection(string value)
    {
        SetComboBoxSelection(TargetLanguageComboBox, value, "自定义目标语言");
    }

    private string GetSelectedTargetLanguage()
    {
        return (TargetLanguageComboBox.SelectedItem as WpfComboBoxItem)?.Tag?.ToString()
            ?? "zh-CN";
    }

    private void SetTesseractLanguageSelection(string value)
    {
        SetComboBoxSelection(TesseractLanguageComboBox, value, "自定义 OCR 语言");
    }

    private string GetSelectedTesseractLanguage()
    {
        return (TesseractLanguageComboBox.SelectedItem as WpfComboBoxItem)?.Tag?.ToString()
            ?? "chi_sim+eng";
    }

    private static void SetComboBoxSelection(WpfComboBox comboBox, string value, string customPrefix)
    {
        foreach (var item in comboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        var customItem = comboBox.Items
            .OfType<WpfComboBoxItem>()
            .FirstOrDefault(item => item.Tag is null);

        if (customItem is null)
        {
            customItem = new WpfComboBoxItem();
            comboBox.Items.Add(customItem);
        }

        customItem.Tag = value;
        customItem.Content = $"{customPrefix}（{value}）";
        comboBox.SelectedItem = customItem;
    }

    private static string ResolveDefaultTranslationProvider(AppSettings settings)
    {
        return SmartTranslationService.HasCustomApiSettings(settings)
            ? TranslationProviderPreference.Api
            : TranslationProviderPreference.Local;
    }

    private static string FormatOcrEngine(string value)
    {
        return value switch
        {
            "windows-media-ocr" => "系统内置 OCR",
            "enhanced-tesseract" => "增强本地 OCR",
            "tesseract-cli" => "兼容模式 OCR",
            _ => value
        };
    }

    private static string FormatOcrSummary(AppSettings settings)
    {
        return settings.OcrEngine switch
        {
            "windows-media-ocr" => $"系统内置 OCR（免安装，lang={FormatSummaryValue(settings.TesseractLanguage)})",
            _ => $"{FormatOcrEngine(settings.OcrEngine)} ({FormatSummaryValue(settings.TesseractExecutablePath)}, lang={FormatSummaryValue(settings.TesseractLanguage)})"
        };
    }

    private static string FormatTranslationSummary(AppSettings settings)
    {
        settings.NormalizeApiProfiles();
        var selectedProfile = settings.GetSelectedApiProfile();

        return SmartTranslationService.GetEffectiveProvider(settings) switch
        {
            TranslationProviderPreference.Local => "本地轻量翻译",
            TranslationProviderPreference.Api => $"API 翻译（{FormatSummaryValue(selectedProfile?.Name ?? string.Empty)} / {FormatSummaryValue(selectedProfile?.Model ?? string.Empty)}）",
            _ => "本地轻量翻译"
        };
    }

    private static string FormatTranslationProvider(string value)
    {
        return value switch
        {
            TranslationProviderPreference.Local => "本地轻量翻译",
            TranslationProviderPreference.Api => "API 翻译",
            _ => "本地轻量翻译"
        };
    }

    private static string FormatTrayLeftClickAction(string value)
    {
        return Enum.TryParse<CaptureWorkflowKind>(value, out var action)
            ? action switch
            {
                CaptureWorkflowKind.CaptureAndPin => "自由框选并固定到屏幕",
                CaptureWorkflowKind.CaptureAndTranslate => "自由框选后自动翻译",
                CaptureWorkflowKind.CaptureAndSave => "自由框选并保存到默认位置",
                _ => "自由框选后等待操作选择"
            }
            : "自由框选后等待操作选择";
    }

    private static string FormatWorkflow(string value)
    {
        return CaptureWorkflowFormatter.ToDisplayName(value);
    }

    private static string FormatSummaryValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未填写" : value;
    }

    private static string FormatRetentionDays(int days)
    {
        return days <= 0 ? "不自动清理" : $"{days} 天";
    }

    private static AppSettings CloneSettings(AppSettings settings) => new()
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
