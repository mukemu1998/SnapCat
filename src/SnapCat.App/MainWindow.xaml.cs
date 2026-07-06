using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapCat.App.Services;
using SnapCat.App.ViewModels;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using SnapCat.Infrastructure.Services;
using Clipboard = System.Windows.Clipboard;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontFamily = System.Drawing.FontFamily;
using DrawingFontStyle = System.Drawing.FontStyle;
using MediaColor = System.Windows.Media.Color;
using WpfApplication = System.Windows.Application;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace SnapCat.App;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly MainWindowViewModel _viewModel;
    private readonly Dictionary<MainSection, NavigationSectionMetadata> _sections = new();
    private AppSettings _settings = new();
    private bool _isExitRequested;
    private bool _startupTrayModePending = true;
    private MainSection _currentSection = MainSection.Status;
    private IReadOnlyList<HotkeyRegistrationResult> _hotkeyRegistrationResults = Array.Empty<HotkeyRegistrationResult>();
    private WpfTextBox? _recordingHotkeyTextBox;
    private string _recordingHotkeyLabel = string.Empty;
    private string _recordingOriginalValue = string.Empty;
    private bool _translationProviderSelectionTouched;
    private bool _suppressTranslationProviderEvents;
    private bool _hotkeyHostReady;
    private bool _isApiKeyVisible;
    private bool _isSyncingApiKeyInputs;
    private bool _isApplyingSettings;
    private bool _hasLoadedSettings;
    private bool _hasUnsavedSettings;
    private bool _isCaptureWorkflowActive;
    private readonly List<string> _operationLogs = [];
    private bool _isApplyingApiProfileState;

    public MainWindow()
    {
        InitializeComponent();
        _app = (App)WpfApplication.Current;
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        WindowBackdropService.ApplyToWindow(this, WindowBackdropService.BackdropKind.TransientWindow, useSystemRoundedCorners: true);
        ConfigureSections();
        Loaded += MainWindow_OnLoaded;
        SourceInitialized += MainWindow_OnSourceInitialized;
        Closing += MainWindow_OnClosing;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
        BaseUrlTextBox.TextChanged += TranslationProviderInputs_OnTextChanged;
        ModelTextBox.TextChanged += TranslationProviderInputs_OnTextChanged;
        TranslationProviderComboBox.SelectionChanged += TranslationProviderComboBox_OnSelectionChanged;
        ApiKeyTextBox.TextChanged += SettingsInput_OnTextChanged;
        ApiProfileNameTextBox.TextChanged += ApiProfileEditor_OnChanged;
        BaseUrlTextBox.TextChanged += ApiProfileEditor_OnChanged;
        ModelTextBox.TextChanged += ApiProfileEditor_OnChanged;
        SystemPromptTextBox.TextChanged += ApiProfileEditor_OnChanged;
        ApiProfileEnableContextCheckBox.Checked += ApiProfileEditor_OnChanged;
        ApiProfileEnableContextCheckBox.Unchecked += ApiProfileEditor_OnChanged;
        TesseractPathTextBox.TextChanged += SettingsInput_OnTextChanged;
        TargetLanguageComboBox.SelectionChanged += SettingsSelection_OnSelectionChanged;
        TranslationProviderComboBox.SelectionChanged += SettingsSelection_OnSelectionChanged;
        OcrEngineComboBox.SelectionChanged += SettingsSelection_OnSelectionChanged;
        TesseractLanguageComboBox.SelectionChanged += SettingsSelection_OnSelectionChanged;
        TrayLeftClickActionComboBox.SelectionChanged += SettingsSelection_OnSelectionChanged;
        TempRetentionDaysTextBox.TextChanged += SettingsInput_OnTextChanged;
        HistoryRetentionDaysTextBox.TextChanged += SettingsInput_OnTextChanged;
        LaunchAtStartupCheckBox.Checked += SettingsToggle_OnChanged;
        LaunchAtStartupCheckBox.Unchecked += SettingsToggle_OnChanged;
        StateChanged += MainWindow_OnStateChanged;
        UpdateApiKeyVisibility(false);
    }

    public void StartInTray()
    {
        if (!_startupTrayModePending)
        {
            return;
        }

        _startupTrayModePending = false;
        Dispatcher.BeginInvoke(() =>
        {
            if (_isExitRequested)
            {
                return;
            }

            HideMainWindow();
            StatusTextBlock.Text = "SnapCat 已在托盘运行，可使用快捷键或托盘菜单开始自由框选。";
        }, DispatcherPriority.ApplicationIdle);
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        CenterOnPrimaryScreen();
        _viewModel.SetVersion(GetAppVersion());

        _settings = TranslationLanguageHelper.CloneSettings(_app.StartupSettingsSnapshot);
        _settings.LaunchAtStartup = _app.StartupRegistrationService.IsEnabled();

        ApplySettingsToControls(_settings);
        _hasLoadedSettings = true;
        MarkSettingsClean();
        RegisterHotkeys();
        ValidateHotkeyConflicts();
        RenderSettingsSummary();
        RenderUserConfigLocationInfo();
        OcrTestResultTextBox.Text = "可以先调整 OCR 相关配置，再点击“测试 OCR”做一次实际验证。";
        TranslationTestResultTextBox.Text = "可以先调整翻译相关配置，再点击“测试 API 连接”或“测试翻译”做一次实际验证。";

        SelectSection(MainSection.Status);

        if (!string.IsNullOrWhiteSpace(_app.StartupSettingsWarning))
        {
            StatusTextBlock.Text = _app.StartupSettingsWarning;
        }

        Dispatcher.BeginInvoke(async () =>
        {
            RenderEnvironmentChecks();
            await LoadHistoryAsync();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _app.GlobalHotkeyService.Attach(this);
        _hotkeyHostReady = true;
        RegisterHotkeys();
        InitializeTray();
    }

    private async void SaveSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await TryApplySettingsAsync(BuildCurrentSettings()))
        {
            return;
        }

        StatusTextBlock.Text = "设置已保存。";
    }

    private async void CaptureButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartCaptureWorkflowAsync(
            CaptureWorkflowKind.CaptureAndWaitForAction,
            returnToMainWindow: true,
            hideMainWindowForCapture: false);
    }

    private async void RunPinActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndPin, returnToMainWindow: true, hideMainWindowForCapture: false);
    }

    private async void RunTranslateActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndTranslate, returnToMainWindow: true, hideMainWindowForCapture: false);
    }

    private async void RunWaitActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndWaitForAction, returnToMainWindow: true, hideMainWindowForCapture: false);
    }

    private async void RunSaveActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndSave, returnToMainWindow: true, hideMainWindowForCapture: false);
    }

    private void OpenUserConfigDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenDirectory(_app.UserDataDirectory, "用户配置目录");
    }

    private void ChooseUserConfigDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new FormsFolderBrowserDialog
        {
            Description = "选择 SnapCat 用户配置保存目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_app.UserDataDirectory)
                ? _app.UserDataDirectory
                : _app.UserDataLocationService.DefaultDirectory,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() != FormsDialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        try
        {
            SavePendingSettingsBeforeDismiss();
            _app.UserDataLocationService.CopyUserData(_app.UserDataDirectory, dialog.SelectedPath);
            _app.UserDataLocationService.SaveCustomDirectory(dialog.SelectedPath);
            RenderUserConfigLocationInfo();
            StatusTextBlock.Text = "用户配置目录已设置，当前配置已复制到新目录。重启 SnapCat 后生效。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"设置用户配置目录失败：{ex.Message}";
        }
    }

    private void ResetUserConfigDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SavePendingSettingsBeforeDismiss();
            _app.UserDataLocationService.CopyUserData(_app.UserDataDirectory, _app.UserDataLocationService.DefaultDirectory);
            _app.UserDataLocationService.ResetToDefaultDirectory();
            RenderUserConfigLocationInfo();
            StatusTextBlock.Text = "用户配置目录已恢复为默认位置，重启 SnapCat 后生效。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"恢复默认用户配置目录失败：{ex.Message}";
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T target)
            {
                return target;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

}
