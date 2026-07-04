using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapCat.App.Services;
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

namespace SnapCat.App;

public partial class MainWindow : Window
{
    private readonly App _app;
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
    private List<ApiTranslationProfile> _editingApiProfiles = [];
    private string _selectedApiProfileId = string.Empty;
    private bool _isApplyingApiProfileState;

    public MainWindow()
    {
        InitializeComponent();
        _app = (App)WpfApplication.Current;
        WindowBackdropService.ApplyToWindow(this, WindowBackdropService.BackdropKind.TransientWindow, useSystemRoundedCorners: true);
        ConfigureSections();
        Loaded += MainWindow_OnLoaded;
        SourceInitialized += MainWindow_OnSourceInitialized;
        Closing += MainWindow_OnClosing;
        PreviewKeyDown += MainWindow_OnPreviewKeyDown;
        BaseUrlTextBox.TextChanged += TranslationProviderInputs_OnTextChanged;
        ModelTextBox.TextChanged += TranslationProviderInputs_OnTextChanged;
        TranslationProviderComboBox.SelectionChanged += TranslationProviderComboBox_OnSelectionChanged;
        BaseUrlTextBox.TextChanged += SettingsInput_OnTextChanged;
        ApiKeyTextBox.TextChanged += SettingsInput_OnTextChanged;
        ApiProfileNameTextBox.TextChanged += ApiProfileNameTextBox_OnTextChanged;
        ModelTextBox.TextChanged += ApiProfileModelTextBox_OnTextChanged;
        ModelTextBox.TextChanged += SettingsInput_OnTextChanged;
        SystemPromptTextBox.TextChanged += SettingsInput_OnTextChanged;
        ApiProfileEnableContextCheckBox.Checked += ApiProfileEnableContextCheckBox_OnChanged;
        ApiProfileEnableContextCheckBox.Unchecked += ApiProfileEnableContextCheckBox_OnChanged;
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
        VersionTextBlock.Text = $"Preview v{GetAppVersion()}";

        _settings = CloneSettings(_app.StartupSettingsSnapshot);
        _settings.LaunchAtStartup = _app.StartupRegistrationService.IsEnabled();

        ApplySettingsToControls(_settings);
        _hasLoadedSettings = true;
        UpdateSaveButtonVisibility();
        RegisterHotkeys();
        ValidateHotkeyConflicts();
        RenderSettingsSummary();
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

    private void ConfigureSections()
    {
        _sections[MainSection.OcrSettings] = new NavigationSectionMetadata(
            OcrNavButton,
            OcrSection,
            "OCR 设置",
            "这里集中管理 OCR 引擎、本地路径和识别测试，不再弹出新的设置窗口。");

        _sections[MainSection.TranslationSettings] = new NavigationSectionMetadata(
            TranslationNavButton,
            TranslationSection,
            "翻译设置",
            "这里集中管理接口地址、模型、目标语言和翻译测试，默认翻译与自定义 API 都从这里处理。");

        _sections[MainSection.History] = new NavigationSectionMetadata(
            HistoryNavButton,
            HistorySection,
            "历史记录",
            "这里直接查看截图历史、结果预览和图片位置，双击或点按钮可继续查看完整详情。");

        _sections[MainSection.PinnedImages] = new NavigationSectionMetadata(
            PinnedImagesNavButton,
            PinnedImagesSection,
            "贴图管理",
            "这里管理当前屏幕上的贴图窗口、贴图组和显示状态。");

        _sections[MainSection.ScreenshotManagement] = new NavigationSectionMetadata(
            ScreenshotManagementNavButton,
            ScreenshotManagementSection,
            "截图管理",
            "这里管理默认截图目录、临时文件目录和过期数据清理。");

        _sections[MainSection.ExecuteActions] = new NavigationSectionMetadata(
            ExecuteActionsNavButton,
            ExecuteActionsSection,
            "执行操作与快捷键",
            "这里集中管理可直接执行的自由框选命令和贴图快捷键，每个命令可以独立绑定快捷键。");

        _sections[MainSection.CaptureSettings] = new NavigationSectionMetadata(
            CaptureSettingsNavButton,
            CaptureSettingsSection,
            "系统快捷键",
            "这里整理主窗口、托盘和系统级操作快捷键；截图与贴图命令快捷键在“执行操作与快捷键”目录里设置。");

        _sections[MainSection.AppearanceSettings] = new NavigationSectionMetadata(
            AppearanceSettingsNavButton,
            AppearanceSettingsSection,
            "外观主题",
            "这里集中调整 SnapCat 的界面配色主题，切换后会立即同步到主菜单、托盘菜单和翻译浮窗。");

        _sections[MainSection.TraySettings] = new NavigationSectionMetadata(
            TraySettingsNavButton,
            TraySettingsSection,
            "托盘与启动",
            "这里整理托盘左键动作、开机自启和启动方式，系统行为都在当前窗口查看和修改。");

        _sections[MainSection.Status] = new NavigationSectionMetadata(
            StatusNavButton,
            StatusSection,
            "运行状态",
            "这里查看当前配置摘要、环境检查和当前主窗口的整体工作状态。");
    }

    private void NavigationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string tag)
        {
            return;
        }

        if (Enum.TryParse<MainSection>(tag, out var section))
        {
            SelectSection(section);
        }
    }

    private void SelectSection(MainSection section)
    {
        _currentSection = section;

        foreach (var pair in _sections)
        {
            var isSelected = pair.Key == section;
            pair.Value.Content.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            ApplyNavigationStyle(pair.Value.Button, isSelected);
        }

        var selected = _sections[section];
        SectionTitleTextBlock.Text = selected.Title;
        SectionDescriptionTextBlock.Text = selected.Description;

        if (section == MainSection.PinnedImages)
        {
            RefreshPinnedImagesList();
        }

        if (section == MainSection.ScreenshotManagement)
        {
            RenderScreenshotManagementInfo();
        }
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
            returnToMainWindow: true);
    }

    private async void RunPinActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndPin, returnToMainWindow: true);
    }

    private async void RunTranslateActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndTranslate, returnToMainWindow: true);
    }

    private async void RunWaitActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndWaitForAction, returnToMainWindow: true);
    }

    private async void RunSaveActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        await StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndSave, returnToMainWindow: true);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        HideMainWindow();
    }

    private async void RefreshHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
        StatusTextBlock.Text = "历史记录已刷新。";
    }

    private void OpenHistoryDetailButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedHistoryDetail();
    }

    private void OpenImageLocationButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedImageLocation();
    }

    private async void DeleteHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedHistoryAsync();
    }

    private async void ClearHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ClearHistoryAsync();
    }

    private void CleanupTempNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = BuildCurrentSettings();
        var deletedTempCount = _app.CapturedImageFileService.CleanupTempFilesOlderThan(settings.TempFileRetentionDays);

        StatusTextBlock.Text = $"已清理 {deletedTempCount} 个过期临时文件。";
    }

    private void OpenDefaultCaptureDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenDirectory(_app.CapturedImageFileService.GetDefaultDirectoryPath(), "默认截图目录");
    }

    private void OpenTempCaptureDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenDirectory(_app.CapturedImageFileService.GetTempDirectoryPath(), "临时文件目录");
    }

    private void RefreshDefaultCapturesButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshDefaultCapturesList();
        StatusTextBlock.Text = "默认保存截图列表已刷新。";
    }

    private void SelectAllDefaultCapturesButton_OnClick(object sender, RoutedEventArgs e)
    {
        DefaultCapturesListBox.SelectAll();
    }

    private void DeleteSelectedDefaultCapturesButton_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteSelectedDefaultCaptures();
    }

    private void DefaultCapturesListBox_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var item = FindParent<ListBoxItem>(source);
        if (item is null)
        {
            return;
        }

        if (!item.IsSelected)
        {
            DefaultCapturesListBox.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void OpenSelectedDefaultCaptureLocationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedDefaultCaptureLocation();
    }

    private void DeleteSelectedDefaultCapturesMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteSelectedDefaultCaptures();
    }

    private void HistoryListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<ListBoxItem>(source) is not null)
        {
            OpenSelectedHistoryDetail();
        }
    }

    private void HistoryListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderHistoryPreview(HistoryListBox.SelectedItem as HistoryListItem);
    }

    private void CopyHistoryPrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(HistoryPrimaryTextBox.Text ?? string.Empty);
        StatusTextBlock.Text = "已复制左侧预览内容。";
    }

    private void CopyHistorySecondaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(HistorySecondaryTextBox.Text ?? string.Empty);
        StatusTextBlock.Text = "已复制右侧预览内容。";
    }

    private void RefreshPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "贴图列表已刷新。";
    }

    private void SelectAllPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        PinnedImagesListBox.SelectAll();
    }

    private void DeleteSelectedPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var ids = PinnedImagesListBox.SelectedItems
            .OfType<PinnedImageListItem>()
            .Select(item => item.Id)
            .ToList();

        _app.PinnedWindowRegistryService.CloseSnapshots(ids);
        RefreshPinnedImagesList();
        StatusTextBlock.Text = ids.Count == 0 ? "请先选择要删除的贴图。" : $"已删除 {ids.Count} 个贴图。";
    }

    private void DeleteAllPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.CloseAllWindows();
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "已删除全部贴图。";
    }

    private void ShowAllPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowAllPinnedImages();
    }

    private void HideAllPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        HideAllPinnedImages();
    }

    private void ShowUngroupedPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowUngroupedPinnedImages();
    }

    private void ShowPinnedGroupOneButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowPinnedGroup(PinnedWindowRegistryService.GroupOneName);
    }

    private void ShowPinnedGroupTwoButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowPinnedGroup(PinnedWindowRegistryService.GroupTwoName);
    }

    private void ShowPinnedGroupThreeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowPinnedGroup(PinnedWindowRegistryService.GroupThreeName);
    }

    private void ShowPinnedGroup(string groupName)
    {
        _app.PinnedWindowRegistryService.ShowGroup(groupName);
        RefreshPinnedImagesList();
        StatusTextBlock.Text = $"已显示{groupName}。";
    }

    private void ShowAllPinnedImages()
    {
        _app.PinnedWindowRegistryService.ShowAllWindows();
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "已显示全部贴图。";
    }

    private void HideAllPinnedImages()
    {
        _app.PinnedWindowRegistryService.HideAllWindows();
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "已隐藏全部贴图。";
    }

    private void ShowUngroupedPinnedImages()
    {
        _app.PinnedWindowRegistryService.ShowUngroupedWindows();
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "已显示未成组贴图。";
    }

    private void PinnedImagesListBox_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var item = FindParent<ListBoxItem>(source);
        if (item is null)
        {
            return;
        }

        if (!item.IsSelected)
        {
            PinnedImagesListBox.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void AssignPinnedImagesToUngroupedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        AssignSelectedPinnedImagesToGroup(PinnedWindowRegistryService.UngroupedGroupName);
    }

    private void AssignPinnedImagesToGroupOneMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        AssignSelectedPinnedImagesToGroup(PinnedWindowRegistryService.GroupOneName);
    }

    private void AssignPinnedImagesToGroupTwoMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        AssignSelectedPinnedImagesToGroup(PinnedWindowRegistryService.GroupTwoName);
    }

    private void AssignPinnedImagesToGroupThreeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        AssignSelectedPinnedImagesToGroup(PinnedWindowRegistryService.GroupThreeName);
    }

    private void DeleteSelectedPinnedImagesMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteSelectedPinnedImagesButton_OnClick(sender, e);
    }

    private void AssignSelectedPinnedImagesToGroup(string groupName)
    {
        var ids = GetSelectedPinnedImageIds();
        _app.PinnedWindowRegistryService.SetSnapshotsGroup(ids, groupName);
        RefreshPinnedImagesList();
        StatusTextBlock.Text = ids.Count == 0
            ? "请先选择要指定分组的贴图。"
            : $"已更新 {ids.Count} 个贴图的分组。";
    }

    private void RefreshPinnedImagesList()
    {
        PinnedImagesListBox.ItemsSource = _app.PinnedWindowRegistryService
            .GetActiveSnapshots()
            .Select(static snapshot => new PinnedImageListItem(snapshot))
            .ToList();
    }

    private List<string> GetSelectedPinnedImageIds()
    {
        return PinnedImagesListBox.SelectedItems
            .OfType<PinnedImageListItem>()
            .Select(item => item.Id)
            .ToList();
    }

    private void RefreshDefaultCapturesList()
    {
        var directory = _app.CapturedImageFileService.GetDefaultDirectoryPath();
        if (!Directory.Exists(directory))
        {
            DefaultCapturesListBox.ItemsSource = Array.Empty<DefaultCaptureListItem>();
            return;
        }

        DefaultCapturesListBox.ItemsSource = Directory
            .EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTime)
            .Select(static path => new DefaultCaptureListItem(path))
            .ToList();
    }

    private void OpenSelectedDefaultCaptureLocation()
    {
        if (DefaultCapturesListBox.SelectedItem is not DefaultCaptureListItem item || !File.Exists(item.Path))
        {
            StatusTextBlock.Text = "请先选择要打开位置的截图。";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{item.Path}\"",
            UseShellExecute = true
        });
    }

    private void DeleteSelectedDefaultCaptures()
    {
        var defaultDirectory = Path.GetFullPath(_app.CapturedImageFileService.GetDefaultDirectoryPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var selectedItems = DefaultCapturesListBox.SelectedItems
            .OfType<DefaultCaptureListItem>()
            .ToList();

        var deletedCount = 0;
        foreach (var item in selectedItems)
        {
            try
            {
                var fullPath = Path.GetFullPath(item.Path);
                if (fullPath.StartsWith(defaultDirectory, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    deletedCount++;
                }
            }
            catch
            {
                // 文件可能正被外部程序占用，跳过即可。
            }
        }

        RefreshDefaultCapturesList();
        StatusTextBlock.Text = selectedItems.Count == 0
            ? "请先选择要删除的截图。"
            : $"已删除 {deletedCount} 个默认保存截图。";
    }

    private async void TestOcrButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = BuildCurrentSettings();
        SetTestButtonsEnabled(false);
        OcrTestResultTextBox.Text = "正在测试 OCR，请稍候...";

        var tempFile = string.Empty;

        try
        {
            tempFile = CreateOcrTestImage();
            var result = await _app.OcrService.RecognizeAsync(tempFile, settings);

            OcrTestResultTextBox.Text = result.Success
                ? $"OCR 测试成功。\n\n识别结果：\n{result.Text}\n\n调试信息：\n{result.DebugSummary}"
                : $"OCR 测试失败。\n\n错误信息：\n{result.ErrorMessage}\n\n调试信息：\n{result.DebugSummary}";
        }
        catch (Exception ex)
        {
            OcrTestResultTextBox.Text = $"OCR 测试执行失败：{ex.Message}";
        }
        finally
        {
            TryDeleteFile(tempFile);
            SetTestButtonsEnabled(true);
        }
    }

    private async void TestTranslationButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = BuildCurrentSettings();
        SetTestButtonsEnabled(false);
        TranslationTestResultTextBox.Text = "正在测试翻译接口，请稍候...";

        try
        {
            const string sourceText = "Hello from SnapCat. This is a translation test.";
            var result = await _app.TranslationService.TranslateAsync(sourceText, settings);

            TranslationTestResultTextBox.Text = result.Success
                ? $"翻译测试成功。\n\n原文：\n{sourceText}\n\n译文：\n{result.Text}"
                : $"翻译测试失败。\n\n错误信息：\n{result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            TranslationTestResultTextBox.Text = $"翻译测试执行失败：{ex.Message}";
        }
        finally
        {
            SetTestButtonsEnabled(true);
        }
    }

    private async void TestApiConnectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = BuildCurrentSettings();
        SetTestButtonsEnabled(false);

        if (!SmartTranslationService.HasCustomApiSettings(settings))
        {
            var message = settings.ApiProfiles.Count == 0
                ? "API 连接测试前请先添加一套 API 配置。"
                : "API 连接测试前请先填写完整的 API Key 和模型。";
            TranslationTestResultTextBox.Text = message;
            StatusTextBlock.Text = message;
            SetTestButtonsEnabled(true);
            return;
        }

        TranslationTestResultTextBox.Text = "正在测试 API 连接，请稍候...";
        StatusTextBlock.Text = "正在测试 API 连接...";

        try
        {
            var testSettings = CloneSettings(settings);
            testSettings.TranslationProviderPreference = TranslationProviderPreference.Api;
            testSettings.NormalizeApiProfiles();
            var selectedProfile = testSettings.GetSelectedApiProfile();

            const string sourceText = "SnapCat API connection test.";
            var result = await _app.TranslationService.TranslateAsync(sourceText, testSettings);

            if (result.Success)
            {
                var message =
                    $"API 连接测试成功。\n\n配置名称：{FormatSummaryValue(selectedProfile?.Name ?? string.Empty)}\n接口地址：{FormatSummaryValue(selectedProfile?.BaseUrl ?? string.Empty)}\n模型：{FormatSummaryValue(selectedProfile?.Model ?? string.Empty)}\n返回内容：\n{result.Text}";
                TranslationTestResultTextBox.Text = message;
                StatusTextBlock.Text = "API 连接测试成功。";
            }
            else
            {
                var message =
                    $"API 连接测试失败。\n\n配置名称：{FormatSummaryValue(selectedProfile?.Name ?? string.Empty)}\n接口地址：{FormatSummaryValue(selectedProfile?.BaseUrl ?? string.Empty)}\n模型：{FormatSummaryValue(selectedProfile?.Model ?? string.Empty)}\n错误信息：\n{result.ErrorMessage}";
                TranslationTestResultTextBox.Text = message;
                StatusTextBlock.Text = "API 连接测试失败。";
            }
        }
        catch (Exception ex)
        {
            TranslationTestResultTextBox.Text = $"API 连接测试执行失败：{ex.Message}";
            StatusTextBlock.Text = "API 连接测试执行失败。";
        }
        finally
        {
            SetTestButtonsEnabled(true);
        }
    }

    private void RecordPinHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndPinTextBox, "固定到屏幕");
    }

    private void RecordTranslateHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndTranslateTextBox, "自动翻译");
    }

    private void RecordWaitHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndWaitTextBox, "等待操作");
    }

    private void RecordSaveHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndSaveTextBox, "保存截图");
    }

    private void RecordPinnedCloseShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(PinnedCloseShortcutTextBox, "关闭贴图");
    }

    private void RecordPinnedHideShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(PinnedHideShortcutTextBox, "隐藏贴图");
    }

    private void RecordShowAllPinnedHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyShowAllPinnedTextBox, "显示全部贴图");
    }

    private void RecordHideAllPinnedHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyHideAllPinnedTextBox, "隐藏全部贴图");
    }

    private void RecordShowUngroupedPinnedHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyShowUngroupedPinnedTextBox, "显示未成组贴图");
    }

    private void RecordShowPinnedGroupOneHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyShowPinnedGroupOneTextBox, "显示贴图组 1");
    }

    private void RecordShowPinnedGroupTwoHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyShowPinnedGroupTwoTextBox, "显示贴图组 2");
    }

    private void RecordShowPinnedGroupThreeHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyShowPinnedGroupThreeTextBox, "显示贴图组 3");
    }

    private void RecordShowMainWindowHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyShowMainWindowTextBox, "打开主菜单");
    }

    private void RecordExitApplicationHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyExitApplicationTextBox, "退出软件");
    }

    private void ClearPinHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        HotkeyCaptureAndPinTextBox.Text = string.Empty;
        ValidateHotkeyConflicts();
        UpdateSaveButtonVisibility();
    }

    private void ClearTranslateHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        HotkeyCaptureAndTranslateTextBox.Text = string.Empty;
        ValidateHotkeyConflicts();
        UpdateSaveButtonVisibility();
    }

    private void ClearWaitHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        HotkeyCaptureAndWaitTextBox.Text = string.Empty;
        ValidateHotkeyConflicts();
        UpdateSaveButtonVisibility();
    }

    private void ClearSaveHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        HotkeyCaptureAndSaveTextBox.Text = string.Empty;
        ValidateHotkeyConflicts();
        UpdateSaveButtonVisibility();
    }

    private void ClearPinnedCloseShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        PinnedCloseShortcutTextBox.Text = string.Empty;
        ValidateHotkeyConflicts();
        UpdateSaveButtonVisibility();
    }

    private void ClearPinnedHideShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        PinnedHideShortcutTextBox.Text = string.Empty;
        ValidateHotkeyConflicts();
        UpdateSaveButtonVisibility();
    }

    private void ClearShowAllPinnedHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyShowAllPinnedTextBox);
    }

    private void ClearHideAllPinnedHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyHideAllPinnedTextBox);
    }

    private void ClearShowUngroupedPinnedHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyShowUngroupedPinnedTextBox);
    }

    private void ClearShowPinnedGroupOneHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyShowPinnedGroupOneTextBox);
    }

    private void ClearShowPinnedGroupTwoHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyShowPinnedGroupTwoTextBox);
    }

    private void ClearShowPinnedGroupThreeHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyShowPinnedGroupThreeTextBox);
    }

    private void ClearShowMainWindowHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyShowMainWindowTextBox);
    }

    private void ClearExitApplicationHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyExitApplicationTextBox);
    }

    private void ClearHotkeyTextBox(WpfTextBox textBox)
    {
        textBox.Text = string.Empty;
        ValidateHotkeyConflicts();
        UpdateSaveButtonVisibility();
    }

    private void RestoreDefaultHotkeysButton_OnClick(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        HotkeyCaptureAndPinTextBox.Text = defaults.HotkeyCaptureAndPin;
        HotkeyCaptureAndTranslateTextBox.Text = defaults.HotkeyCaptureAndTranslate;
        HotkeyCaptureAndWaitTextBox.Text = defaults.HotkeyCaptureAndWaitForAction;
        HotkeyCaptureAndSaveTextBox.Text = defaults.HotkeyCaptureAndSave;
        PinnedCloseShortcutTextBox.Text = defaults.PinnedCloseShortcut;
        PinnedHideShortcutTextBox.Text = defaults.PinnedHideShortcut;
        HotkeyShowAllPinnedTextBox.Text = defaults.HotkeyShowAllPinned;
        HotkeyHideAllPinnedTextBox.Text = defaults.HotkeyHideAllPinned;
        HotkeyShowUngroupedPinnedTextBox.Text = defaults.HotkeyShowUngroupedPinned;
        HotkeyShowPinnedGroupOneTextBox.Text = defaults.HotkeyShowPinnedGroupOne;
        HotkeyShowPinnedGroupTwoTextBox.Text = defaults.HotkeyShowPinnedGroupTwo;
        HotkeyShowPinnedGroupThreeTextBox.Text = defaults.HotkeyShowPinnedGroupThree;
        HotkeyShowMainWindowTextBox.Text = defaults.HotkeyShowMainWindow;
        HotkeyExitApplicationTextBox.Text = defaults.HotkeyExitApplication;
        ValidateHotkeyConflicts();
        UpdateSaveButtonVisibility();
        StatusTextBlock.Text = "已还原默认快捷键。";
    }

    private void BeginHotkeyRecording(WpfTextBox target, string label)
    {
        _recordingHotkeyTextBox = target;
        _recordingHotkeyLabel = label;
        _recordingOriginalValue = target.Text;
        target.Text = "请按下快捷键，按 Esc 取消";
        Focus();
        Keyboard.Focus(this);
    }

    private void MainWindow_OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (_recordingHotkeyTextBox is null)
        {
            return;
        }

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            _recordingHotkeyTextBox.Text = _recordingOriginalValue;
            StatusTextBlock.Text = $"已取消“{_recordingHotkeyLabel}”快捷键录制。";
            ResetHotkeyRecordingState();
            ValidateHotkeyConflicts();
            return;
        }

        if (IsModifierOnlyKey(key))
        {
            _recordingHotkeyTextBox.Text = "请继续按下主键...";
            return;
        }

        var hotkeyText = FormatHotkeyText(key, Keyboard.Modifiers);
        _recordingHotkeyTextBox.Text = hotkeyText;
        StatusTextBlock.Text = $"已录制“{_recordingHotkeyLabel}”快捷键：{hotkeyText}";
        ResetHotkeyRecordingState();
        ValidateHotkeyConflicts();
        UpdateSaveButtonVisibility();
    }

    private void ResetHotkeyRecordingState()
    {
        _recordingHotkeyTextBox = null;
        _recordingHotkeyLabel = string.Empty;
        _recordingOriginalValue = string.Empty;
    }

    private void ValidateHotkeyConflicts()
    {
        var hotkeys = new Dictionary<string, string>
        {
            ["固定到屏幕"] = HotkeyCaptureAndPinTextBox.Text.Trim(),
            ["自动翻译"] = HotkeyCaptureAndTranslateTextBox.Text.Trim(),
            ["等待操作"] = HotkeyCaptureAndWaitTextBox.Text.Trim(),
            ["保存截图"] = HotkeyCaptureAndSaveTextBox.Text.Trim(),
            ["关闭贴图"] = PinnedCloseShortcutTextBox.Text.Trim(),
            ["隐藏贴图"] = PinnedHideShortcutTextBox.Text.Trim(),
            ["显示全部贴图"] = HotkeyShowAllPinnedTextBox.Text.Trim(),
            ["隐藏全部贴图"] = HotkeyHideAllPinnedTextBox.Text.Trim(),
            ["显示未成组贴图"] = HotkeyShowUngroupedPinnedTextBox.Text.Trim(),
            ["显示贴图组 1"] = HotkeyShowPinnedGroupOneTextBox.Text.Trim(),
            ["显示贴图组 2"] = HotkeyShowPinnedGroupTwoTextBox.Text.Trim(),
            ["显示贴图组 3"] = HotkeyShowPinnedGroupThreeTextBox.Text.Trim(),
            ["打开主菜单"] = HotkeyShowMainWindowTextBox.Text.Trim(),
            ["退出软件"] = HotkeyExitApplicationTextBox.Text.Trim()
        };

        var duplicates = hotkeys
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(static pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .ToList();

        var registrationFailures = _hotkeyRegistrationResults
            .Where(static result => !result.IsRegistered)
            .ToList();

        if (duplicates.Count == 0 && registrationFailures.Count == 0)
        {
            HotkeyValidationTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(22, 101, 52));
            HotkeyValidationTextBlock.Text = hotkeys.Any(static pair => !string.IsNullOrWhiteSpace(pair.Value))
                ? "当前快捷键没有发现重复冲突。"
                : "当前没有设置可选快捷键，需要时可在上方录制。";
            return;
        }

        if (duplicates.Count == 0)
        {
            HotkeyValidationTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(185, 28, 28));
            HotkeyValidationTextBlock.Text = string.Join(
                Environment.NewLine,
                registrationFailures.Select(result =>
                    $"注册失败：{result.Label} ({FormatSummaryValue(result.HotkeyText)}) - {result.Message}"));
            return;
        }

        var messages = duplicates.Select(group =>
        {
            var labels = string.Join("、", group.Select(static pair => pair.Key));
            return $"快捷键冲突：{labels} 都使用了 {group.Key}";
        });

        if (registrationFailures.Count > 0)
        {
            messages = messages.Concat(registrationFailures.Select(result =>
                $"注册失败：{result.Label} ({FormatSummaryValue(result.HotkeyText)}) - {result.Message}"));
        }

        HotkeyValidationTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(185, 28, 28));
        HotkeyValidationTextBlock.Text = string.Join(Environment.NewLine, messages);
    }

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

    private void TranslationProviderInputs_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTranslationProviderEvents || _translationProviderSelectionTouched)
        {
            return;
        }

        SetTranslationProviderSelection(ResolveDefaultTranslationProvider(BuildCurrentSettings()));
    }

    private void ApplyApiProfileState()
    {
        _isApplyingApiProfileState = true;

        if (_editingApiProfiles.Count == 0)
        {
            _selectedApiProfileId = string.Empty;
            ApiProfileCardsListBox.ItemsSource = null;
            ApiProfileManagerGrid.Visibility = Visibility.Collapsed;
            EmptyApiProfileStatePanel.Visibility = Visibility.Visible;
            DeleteApiProfileButton.Visibility = Visibility.Collapsed;
            SetApiEditorVisibility(Visibility.Collapsed);
            ClearApiProfileEditor();
            UpdateApiKeyVisibility(false);
            _isApplyingApiProfileState = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedApiProfileId)
            || _editingApiProfiles.All(profile => !string.Equals(profile.Id, _selectedApiProfileId, StringComparison.Ordinal)))
        {
            _selectedApiProfileId = _editingApiProfiles[0].Id;
        }

        ApiProfileCardsListBox.ItemsSource = null;
        ApiProfileCardsListBox.ItemsSource = _editingApiProfiles;
        ApiProfileCardsListBox.SelectedValue = _selectedApiProfileId;
        ApiProfileManagerGrid.Visibility = Visibility.Visible;
        EmptyApiProfileStatePanel.Visibility = Visibility.Collapsed;
        DeleteApiProfileButton.Visibility = Visibility.Visible;
        SetApiEditorVisibility(Visibility.Visible);
        LoadSelectedApiProfileIntoEditor();
        UpdateApiKeyVisibility(false);
        _isApplyingApiProfileState = false;
    }

    private void LoadSelectedApiProfileIntoEditor()
    {
        var profile = GetSelectedEditingApiProfile();
        if (profile is null)
        {
            ClearApiProfileEditor();
            return;
        }

        ApiProfileNameTextBox.Text = profile.Name;
        BaseUrlTextBox.Text = profile.BaseUrl;
        SetApiKeyValue(profile.ApiKey);
        ModelTextBox.Text = profile.Model;
        SystemPromptTextBox.Text = profile.SystemPrompt;
        ApiProfileEnableContextCheckBox.IsChecked = profile.EnableContext;
    }

    private void PersistCurrentApiProfileEditor()
    {
        var profile = GetSelectedEditingApiProfile();
        if (profile is null)
        {
            return;
        }

        profile.Name = string.IsNullOrWhiteSpace(ApiProfileNameTextBox.Text)
            ? profile.Name
            : ApiProfileNameTextBox.Text.Trim();
        profile.BaseUrl = BaseUrlTextBox.Text.Trim();
        profile.ApiKey = GetCurrentApiKey();
        profile.Model = ModelTextBox.Text.Trim();
        profile.SystemPrompt = string.IsNullOrWhiteSpace(SystemPromptTextBox.Text)
            ? AppSettings.DefaultSystemPrompt
            : SystemPromptTextBox.Text.Trim();
        profile.EnableContext = ApiProfileEnableContextCheckBox.IsChecked == true;
    }

    private ApiTranslationProfile? GetSelectedEditingApiProfile()
    {
        return _editingApiProfiles.FirstOrDefault(profile => string.Equals(profile.Id, _selectedApiProfileId, StringComparison.Ordinal));
    }

    private void ClearApiProfileEditor()
    {
        ApiProfileNameTextBox.Text = string.Empty;
        BaseUrlTextBox.Text = string.Empty;
        SetApiKeyValue(string.Empty);
        ModelTextBox.Text = string.Empty;
        SystemPromptTextBox.Text = AppSettings.DefaultSystemPrompt;
        ApiProfileEnableContextCheckBox.IsChecked = false;
    }

    private string GenerateNextApiProfileName()
    {
        var index = 1;
        while (_editingApiProfiles.Any(profile => string.Equals(profile.Name, $"API 配置 {index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        return $"API 配置 {index}";
    }

    private void SetApiEditorVisibility(Visibility visibility)
    {
        BaseUrlLabelTextBlock.Visibility = visibility;
        BaseUrlTextBox.Visibility = visibility;
        ApiKeyLabelTextBlock.Visibility = visibility;
        ApiKeyEditorGrid.Visibility = visibility;
        ModelLabelTextBlock.Visibility = visibility;
        ModelTextBox.Visibility = visibility;
        SystemPromptLabelTextBlock.Visibility = visibility;
        SystemPromptTextBox.Visibility = visibility;
    }

    private void SettingsInput_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSaveButtonVisibility();
    }

    private void SettingsSelection_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSaveButtonVisibility();
    }

    private void SettingsToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        UpdateSaveButtonVisibility();
    }

    private string GetCurrentApiKey()
    {
        return _isApiKeyVisible ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password;
    }

    private static int ParseRetentionDays(string? value, int fallback)
    {
        return int.TryParse(value?.Trim(), out var days)
            ? Math.Max(0, days)
            : Math.Max(0, fallback);
    }

    private void SetApiKeyValue(string value)
    {
        _isSyncingApiKeyInputs = true;
        ApiKeyPasswordBox.Password = value ?? string.Empty;
        ApiKeyTextBox.Text = value ?? string.Empty;
        _isSyncingApiKeyInputs = false;
    }

    private void UpdateApiKeyVisibility(bool isVisible)
    {
        _isApiKeyVisible = isVisible;

        if (ApiKeyPasswordBox is null || ApiKeyTextBox is null || ToggleApiKeyVisibilityButton is null || ApiKeyVisibilityIconTextBlock is null)
        {
            return;
        }

        ApiKeyPasswordBox.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        ApiKeyTextBox.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        ToggleApiKeyVisibilityButton.ToolTip = isVisible ? "隐藏 API Key" : "显示 API Key";
        ApiKeyVisibilityIconTextBlock.Text = isVisible ? "🙈" : "👁";
    }

    private void ToggleApiKeyVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateApiKeyVisibility(!_isApiKeyVisible);
    }

    private void ApiKeyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingApiKeyInputs)
        {
            return;
        }

        _isSyncingApiKeyInputs = true;
        ApiKeyTextBox.Text = ApiKeyPasswordBox.Password;
        _isSyncingApiKeyInputs = false;
        TranslationProviderInputs_OnTextChanged(ApiKeyTextBox, new TextChangedEventArgs(System.Windows.Controls.TextBox.TextChangedEvent, UndoAction.None));
        UpdateSaveButtonVisibility();
    }

    private void ApiKeyTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingApiKeyInputs)
        {
            TranslationProviderInputs_OnTextChanged(sender, e);
            return;
        }

        _isSyncingApiKeyInputs = true;
        ApiKeyPasswordBox.Password = ApiKeyTextBox.Text;
        _isSyncingApiKeyInputs = false;
        TranslationProviderInputs_OnTextChanged(sender, e);
        UpdateSaveButtonVisibility();
    }

    private void ApiProfileNameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        var profile = GetSelectedEditingApiProfile();
        if (profile is not null)
        {
            profile.Name = string.IsNullOrWhiteSpace(ApiProfileNameTextBox.Text)
                ? profile.Name
                : ApiProfileNameTextBox.Text.Trim();
            ApiProfileCardsListBox.Items.Refresh();
        }

        UpdateSaveButtonVisibility();
    }

    private void ApiProfileModelTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        var profile = GetSelectedEditingApiProfile();
        if (profile is not null)
        {
            profile.Model = ModelTextBox.Text.Trim();
            ApiProfileCardsListBox.Items.Refresh();
        }
    }

    private void ApiProfileEnableContextCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        var profile = GetSelectedEditingApiProfile();
        if (profile is not null)
        {
            profile.EnableContext = ApiProfileEnableContextCheckBox.IsChecked == true;
        }

        UpdateSaveButtonVisibility();
    }

    private void TranslationProviderComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTranslationProviderEvents)
        {
            return;
        }

        _translationProviderSelectionTouched = true;
        UpdateSaveButtonVisibility();
    }

    private void AddApiProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        PersistCurrentApiProfileEditor();

        var profile = new ApiTranslationProfile
        {
            Name = GenerateNextApiProfileName(),
            SystemPrompt = AppSettings.DefaultSystemPrompt
        };

        _editingApiProfiles.Add(profile);
        _selectedApiProfileId = profile.Id;
        ApplyApiProfileState();
        UpdateSaveButtonVisibility();
        StatusTextBlock.Text = $"已添加新的 API 配置：{profile.Name}";
    }

    private void DeleteApiProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteApiProfileById(_selectedApiProfileId);
    }

    private void DeleteApiProfileById(string profileId)
    {
        var profile = _editingApiProfiles.FirstOrDefault(item => string.Equals(item.Id, profileId, StringComparison.Ordinal));
        if (profile is null)
        {
            return;
        }

        _editingApiProfiles.RemoveAll(item => string.Equals(item.Id, profile.Id, StringComparison.Ordinal));
        _selectedApiProfileId = _editingApiProfiles.FirstOrDefault()?.Id ?? string.Empty;
        ApplyApiProfileState();

        if (_editingApiProfiles.Count == 0)
        {
            _suppressTranslationProviderEvents = true;
            SetTranslationProviderSelection(TranslationProviderPreference.Local);
            _suppressTranslationProviderEvents = false;
        }

        UpdateSaveButtonVisibility();
        StatusTextBlock.Text = $"已删除 API 配置：{profile.Name}";
    }

    private void ApiProfileCardsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingApiProfileState)
        {
            return;
        }

        PersistCurrentApiProfileEditor();
        _selectedApiProfileId = ApiProfileCardsListBox.SelectedValue?.ToString() ?? string.Empty;
        LoadSelectedApiProfileIntoEditor();
        UpdateSaveButtonVisibility();
    }

    private async Task StartCaptureWorkflowAsync(
        CaptureWorkflowKind workflow,
        bool returnToMainWindow)
    {
        CaptureButton.IsEnabled = false;
        StatusTextBlock.Text = "请在屏幕上进行自由框选。";

        Window? owner = null;
        var wasMinimizedToTaskbar = IsVisible
            && WindowState == WindowState.Minimized
            && ShowInTaskbar;

        try
        {
            if (!wasMinimizedToTaskbar)
            {
                HideMainWindow();
                await Task.Delay(180);
            }

            var overlay = new SelectionOverlayWindow();
            var selectionConfirmed = overlay.ShowDialog() == true && overlay.SelectedRegion is not null;

            if (!selectionConfirmed)
            {
                if (returnToMainWindow)
                {
                    ShowMainWindow();
                }

                StatusTextBlock.Text = "已取消自由框选。";
                return;
            }

            var captureRegion = overlay.SelectedRegion!.Value;
            var action = CaptureActionKind.Cancel;
            var workingCaptureRegion = captureRegion;

            switch (workflow)
            {
                case CaptureWorkflowKind.CaptureAndPin:
                    action = CaptureActionKind.PinToScreen;
                    break;
                case CaptureWorkflowKind.CaptureAndTranslate:
                    action = CaptureActionKind.OcrAndTranslate;
                    break;
                case CaptureWorkflowKind.CaptureAndSave:
                    action = CaptureActionKind.Save;
                    break;
                default:
                {
                    var selection = await SelectActionAsync(captureRegion);
                    action = selection.Action;
                    workingCaptureRegion = selection.CaptureRegion;
                    break;
                }
            }

            if (action == CaptureActionKind.Cancel)
            {
                if (returnToMainWindow)
                {
                    ShowMainWindow();
                }

                StatusTextBlock.Text = "已取消截图后操作。";
                return;
            }

            await Task.Delay(120);
            var workingImagePath = _app.ScreenCaptureService.CaptureToTempFile(workingCaptureRegion);
            if (action == CaptureActionKind.Save)
            {
                var savedPath = await SaveCaptureToDefaultDirectoryAsync(workingImagePath);
                StatusTextBlock.Text = $"截图已保存到默认目录：{savedPath}";
                await LoadHistoryAsync();

                if (returnToMainWindow)
                {
                    ShowMainWindow();
                }

                return;
            }

            if (action is CaptureActionKind.PinToScreen
                or CaptureActionKind.OcrOnly
                or CaptureActionKind.OcrAndTranslate
                or CaptureActionKind.QrCode)
            {
                workingImagePath = _app.CapturedImageFileService.SaveToDefaultDirectory(workingImagePath);
            }

            var status = await _app.CaptureActionService.ExecuteAsync(
                action,
                workingImagePath,
                CloneSettings(_settings),
                owner,
                workingCaptureRegion,
                action == CaptureActionKind.OcrAndTranslate
                    ? () => StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndTranslate, returnToMainWindow: false)
                    : null);

            StatusTextBlock.Text = status;
            await LoadHistoryAsync();

            if (returnToMainWindow)
            {
                ShowMainWindow();
            }
        }
        catch (Exception ex)
        {
            if (returnToMainWindow)
            {
                ShowMainWindow();
            }

            StatusTextBlock.Text = $"执行失败：{ex.Message}";
        }
        finally
        {
            if (wasMinimizedToTaskbar && !returnToMainWindow && !_isExitRequested)
            {
                KeepMainWindowMinimizedInTaskbar();
            }

            CaptureButton.IsEnabled = true;
        }
    }

    private async Task<string> SaveCaptureToDefaultDirectoryAsync(string imagePath)
    {
        var savedPath = _app.CapturedImageFileService.SaveToDefaultDirectory(imagePath);
        await _app.HistoryStore.AppendAsync(new CaptureTranslationRecord
        {
            WorkflowType = "save",
            ImagePath = savedPath
        });

        return savedPath;
    }

    private async Task<CaptureActionSelectionResult> SelectActionAsync(Int32Rect captureRegion)
    {
        var actionWindow = new CaptureActionSelectionWindow(captureRegion);

        return await Task.FromResult(actionWindow.ShowDialog() == true
            ? new CaptureActionSelectionResult(
                actionWindow.SelectedAction,
                actionWindow.SelectedRegion)
            : new CaptureActionSelectionResult(
                CaptureActionKind.Cancel,
                captureRegion));
    }

    private void RegisterHotkeys()
    {
        if (!_hotkeyHostReady)
        {
            _hotkeyRegistrationResults = Array.Empty<HotkeyRegistrationResult>();
            return;
        }

        _hotkeyRegistrationResults = _app.GlobalHotkeyService.RegisterAll(
            _settings,
            () => _ = StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndPin, returnToMainWindow: false),
            () => _ = StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndTranslate, returnToMainWindow: false),
            () => _ = StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndWaitForAction, returnToMainWindow: false),
            () => _ = StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndSave, returnToMainWindow: false),
            ShowAllPinnedImages,
            HideAllPinnedImages,
            ShowUngroupedPinnedImages,
            () => ShowPinnedGroup(PinnedWindowRegistryService.GroupOneName),
            () => ShowPinnedGroup(PinnedWindowRegistryService.GroupTwoName),
            () => ShowPinnedGroup(PinnedWindowRegistryService.GroupThreeName),
            ShowMainWindow,
            ExitApplication);

        var failedCount = _hotkeyRegistrationResults.Count(static result => !result.IsRegistered);
        if (failedCount > 0)
        {
            StatusTextBlock.Text = $"有 {failedCount} 组快捷键注册失败，请到“执行操作与快捷键”或“系统快捷键”查看原因。";
        }
    }

    private void InitializeTray()
    {
        _app.TrayIconService.Initialize(
            () => _settings,
            GetTrayLeftClickAction,
            workflow => _ = StartCaptureWorkflowAsync(workflow, returnToMainWindow: false),
            OpenSettingsFromTray,
            OpenHistoryFromTray,
            ShowMainWindow,
            OpenCaptureDirectoryFromTray,
            _app.PinnedWindowRegistryService.ShowAllWindows,
            _app.PinnedWindowRegistryService.HideAllWindows,
            _app.PinnedWindowRegistryService.ShowUngroupedWindows,
            _app.PinnedWindowRegistryService.ShowGroup,
            ExitApplication);
    }

    private CaptureWorkflowKind GetTrayLeftClickAction()
    {
        return Enum.TryParse<CaptureWorkflowKind>(_settings.TrayLeftClickAction, out var action)
            ? action
            : CaptureWorkflowKind.CaptureAndWaitForAction;
    }

    private void OpenSettingsFromTray()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowMainWindow();
            SelectSection(MainSection.OcrSettings);
            StatusTextBlock.Text = "已打开主窗口设置页。";
        });
    }

    private void OpenHistoryFromTray()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            ShowMainWindow();
            SelectSection(MainSection.History);
            await LoadHistoryAsync();

            if (HistoryListBox.Items.Count > 0 && HistoryListBox.SelectedIndex < 0)
            {
                HistoryListBox.SelectedIndex = 0;
            }

            HistoryListBox.Focus();
            StatusTextBlock.Text = "已打开历史记录。";
        });
    }

    private void ChangeTrayLeftClickActionFromTray(CaptureWorkflowKind action)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                _settings.TrayLeftClickAction = action.ToString();
                await _app.SettingsStore.SaveAsync(_settings);
                ApplySettingsToControls(_settings);
                RenderSettingsSummary();
                StatusTextBlock.Text = $"托盘左键默认动作已切换为：{FormatTrayLeftClickAction(_settings.TrayLeftClickAction)}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"切换托盘左键默认动作失败：{ex.Message}";
            }
        });
    }

    private void OpenCaptureDirectoryFromTray()
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var directory = _app.CapturedImageFileService.GetDefaultDirectoryPath();
                Directory.CreateDirectory(directory);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true
                });

                StatusTextBlock.Text = "已打开 SnapCat 默认截图目录。";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"打开截图目录失败：{ex.Message}";
            }
        });
    }

    private void OpenDirectory(string directory, string label)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });

            StatusTextBlock.Text = $"已打开{label}。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"打开{label}失败：{ex.Message}";
        }
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true;

        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        CenterOnPrimaryScreen();

        if (!IsVisible)
        {
            Show();
        }

        UpdateMaximizeRestoreButtonText();
        Activate();
        Focus();
        Topmost = true;
        Topmost = false;
    }

    private void HideMainWindow()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void KeepMainWindowMinimizedInTaskbar()
    {
        ShowInTaskbar = true;

        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Minimized;
        UpdateMaximizeRestoreButtonText();
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        _app.PreparePinnedWindowsForExit();
        Close();
        WpfApplication.Current.Shutdown();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideMainWindow();
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreButtonText();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        UpdateMaximizeRestoreButtonText();
    }

    private void UpdateMaximizeRestoreButtonText()
    {
        if (MaximizeRestoreButton is null)
        {
            return;
        }

        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void CenterOnPrimaryScreen()
    {
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        Left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
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
            $"托盘左键：{FormatTrayLeftClickAction(_settings.TrayLeftClickAction)}";
    }

    private void RenderScreenshotManagementInfo()
    {
        DefaultCaptureDirectoryTextBlock.Text = _app.CapturedImageFileService.GetDefaultDirectoryPath();
        TempCaptureDirectoryTextBlock.Text = _app.CapturedImageFileService.GetTempDirectoryPath();
        RefreshDefaultCapturesList();
    }

    private void RenderEnvironmentChecks()
    {
        var warnings = _app.StartupDiagnosticsService.BuildWarnings(_settings);
        if (warnings.Count == 0)
        {
            EnvironmentCheckTitleTextBlock.Text = "启动检查";
            EnvironmentCheckTitleTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(22, 101, 52));
            EnvironmentCheckTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(22, 101, 52));
            EnvironmentCheckTextBlock.Text = "环境检查通过，OCR、翻译配置和快捷键格式看起来都正常。";
            return;
        }

        EnvironmentCheckTitleTextBlock.Text = $"启动检查：发现 {warnings.Count} 个需要处理的问题";
        EnvironmentCheckTitleTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(146, 64, 14));
        EnvironmentCheckTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(146, 64, 14));
        EnvironmentCheckTextBlock.Text = string.Join(Environment.NewLine, warnings.Select(static warning => $"• {warning}"));
    }

    private async Task LoadHistoryAsync()
    {
        var selectedRecordId = (HistoryListBox.SelectedItem as HistoryListItem)?.Record.RecordId;
        var recent = await _app.HistoryStore.LoadRecentAsync(20);
        var items = recent.Select(record => new HistoryListItem(record)).ToList();
        HistoryListBox.ItemsSource = items;

        if (items.Count == 0)
        {
            HistoryListBox.SelectedItem = null;
            RenderHistoryPreview(null);
            return;
        }

        var selectedItem = items.FirstOrDefault(item => string.Equals(item.Record.RecordId, selectedRecordId, StringComparison.Ordinal))
            ?? items[0];

        HistoryListBox.SelectedItem = selectedItem;
        RenderHistoryPreview(selectedItem);
    }

    private void OpenSelectedHistoryDetail()
    {
        if (HistoryListBox.SelectedItem is not HistoryListItem item)
        {
            StatusTextBlock.Text = "请先在历史记录中选择一项。";
            return;
        }

        var window = BuildHistoryDetailWindow(item.Record)
            ?? BuildUnsupportedHistoryWindow(item.Record);

        window.Owner = this;
        window.ShowDialog();
    }

    private void OpenSelectedImageLocation()
    {
        if (HistoryListBox.SelectedItem is not HistoryListItem item)
        {
            StatusTextBlock.Text = "请先在历史记录中选择一项。";
            return;
        }

        var imagePath = item.Record.ImagePath;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            StatusTextBlock.Text = "该记录没有可用的截图路径。";
            return;
        }

        try
        {
            if (File.Exists(imagePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{imagePath}\"",
                    UseShellExecute = true
                });

                StatusTextBlock.Text = "已打开截图所在位置。";
                return;
            }

            var directory = Path.GetDirectoryName(imagePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true
                });

                StatusTextBlock.Text = "截图文件不存在，已打开所在文件夹。";
                return;
            }

            StatusTextBlock.Text = "截图文件和目录都不存在。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"打开截图位置失败：{ex.Message}";
        }
    }

    private async Task DeleteSelectedHistoryAsync()
    {
        if (HistoryListBox.SelectedItem is not HistoryListItem item)
        {
            StatusTextBlock.Text = "请先在历史记录中选择一项。";
            return;
        }

        var confirm = WpfMessageBox.Show(
            this,
            $"确定删除这条历史记录吗？\n\n{item.Summary}",
            "删除历史记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            StatusTextBlock.Text = "已取消删除。";
            return;
        }

        await _app.HistoryStore.DeleteAsync(item.Record);
        await LoadHistoryAsync();
        StatusTextBlock.Text = "已删除选中的历史记录。";
    }

    private async Task ClearHistoryAsync()
    {
        if (HistoryListBox.Items.Count == 0)
        {
            StatusTextBlock.Text = "当前没有可清空的历史记录。";
            return;
        }

        var confirm = WpfMessageBox.Show(
            this,
            "确定清空全部历史记录吗？此操作不可撤销。",
            "清空历史记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            StatusTextBlock.Text = "已取消清空历史记录。";
            return;
        }

        await _app.HistoryStore.ClearAsync();
        await LoadHistoryAsync();
        StatusTextBlock.Text = "历史记录已清空。";
    }

    private static ResultWindow? BuildHistoryDetailWindow(CaptureTranslationRecord record)
    {
        return record.WorkflowType switch
        {
            "ocr" => new ResultWindow(
                "历史详情 - OCR 识别",
                string.IsNullOrWhiteSpace(record.OcrError) ? "OCR 识别已完成。" : $"OCR 失败：{record.OcrError}",
                "OCR 文本",
                string.IsNullOrWhiteSpace(record.SourceText) ? record.OcrError : record.SourceText,
                "截图路径",
                record.ImagePath,
                record.OcrDebugInfo,
                imagePath: record.ImagePath),
            "ocr-translate" => new ResultWindow(
                "历史详情 - OCR 并翻译",
                BuildHistoryTranslateStatus(record),
                "原文",
                string.IsNullOrWhiteSpace(record.SourceText) ? record.OcrError : record.SourceText,
                "译文",
                string.IsNullOrWhiteSpace(record.TranslatedText) ? record.TranslationError : record.TranslatedText,
                record.OcrDebugInfo,
                imagePath: record.ImagePath),
            "qr" => new ResultWindow(
                "历史详情 - 二维码识别",
                string.IsNullOrWhiteSpace(record.OcrError) ? "二维码识别已完成。" : $"二维码识别失败：{record.OcrError}",
                "二维码内容",
                string.IsNullOrWhiteSpace(record.QrCodeText) ? record.OcrError : record.QrCodeText,
                "截图路径",
                record.ImagePath,
                imagePath: record.ImagePath),
            "pin" => new ResultWindow(
                "历史详情 - 固定到屏幕",
                "这条记录表示该截图曾被固定到屏幕。",
                "截图路径",
                record.ImagePath,
                "备注",
                "固定到屏幕不会额外产生 OCR 或翻译结果。",
                imagePath: record.ImagePath),
            "save" => new ResultWindow(
                "历史详情 - 保存截图",
                "这条记录表示该截图已保存到默认目录。",
                "截图路径",
                record.ImagePath,
                "备注",
                "保存截图不会额外产生 OCR 或翻译结果。",
                imagePath: record.ImagePath),
            _ => null
        };
    }

    private static ResultWindow BuildUnsupportedHistoryWindow(CaptureTranslationRecord record)
    {
        return new ResultWindow(
            "历史详情",
            "该记录类型暂未定义专用详情视图。",
            "记录类型",
            record.WorkflowType,
            "截图路径",
            record.ImagePath,
            record.OcrDebugInfo,
            imagePath: record.ImagePath);
    }

    private static string BuildHistoryTranslateStatus(CaptureTranslationRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.OcrError) && string.IsNullOrWhiteSpace(record.TranslationError))
        {
            return "OCR 和翻译已完成。";
        }

        if (!string.IsNullOrWhiteSpace(record.OcrError))
        {
            return $"OCR 失败：{record.OcrError}";
        }

        return $"翻译失败：{record.TranslationError}";
    }

    private void RenderHistoryPreview(HistoryListItem? item)
    {
        if (item is null)
        {
            HistoryPreviewPanelScrollViewer.Visibility = Visibility.Collapsed;
            HistoryPreviewEmptyTextBlock.Visibility = Visibility.Visible;
            HistoryPreviewTitleTextBlock.Text = string.Empty;
            HistoryPreviewMetaTextBlock.Text = string.Empty;
            HistoryPreviewStatusTextBlock.Text = string.Empty;
            HistoryPrimaryGroupBox.Header = "内容";
            HistoryPrimaryTextBox.Text = string.Empty;
            HistorySecondaryGroupBox.Header = "补充信息";
            HistorySecondaryTextBox.Text = string.Empty;
            ClearHistoryPreviewImage();
            return;
        }

        var preview = BuildHistoryPreviewData(item.Record);

        HistoryPreviewEmptyTextBlock.Visibility = Visibility.Collapsed;
        HistoryPreviewPanelScrollViewer.Visibility = Visibility.Visible;
        HistoryPreviewTitleTextBlock.Text = preview.Title;
        HistoryPreviewMetaTextBlock.Text = preview.Meta;
        HistoryPreviewStatusTextBlock.Text = preview.Status;
        HistoryPrimaryGroupBox.Header = preview.PrimaryHeader;
        HistoryPrimaryTextBox.Text = preview.PrimaryText;
        HistorySecondaryGroupBox.Header = preview.SecondaryHeader;
        HistorySecondaryTextBox.Text = preview.SecondaryText;
        HistoryPreviewPanelScrollViewer.ScrollToHome();
        HistoryPrimaryTextBox.ScrollToHome();
        HistorySecondaryTextBox.ScrollToHome();

        LoadHistoryPreviewImage(item.Record.ImagePath);
    }

    private void LoadHistoryPreviewImage(string? imagePath)
    {
        ClearHistoryPreviewImage();

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            HistoryPreviewUnavailableTextBlock.Text = "这条记录没有关联截图。";
            HistoryPreviewUnavailableTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (!File.Exists(imagePath))
        {
            HistoryPreviewUnavailableTextBlock.Text = $"截图文件不存在：{imagePath}";
            HistoryPreviewUnavailableTextBlock.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath);
            bitmap.EndInit();
            bitmap.Freeze();

            HistoryPreviewImage.Source = bitmap;
            HistoryPreviewScrollViewer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            HistoryPreviewUnavailableTextBlock.Text = $"截图预览加载失败：{ex.Message}";
            HistoryPreviewUnavailableTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void ClearHistoryPreviewImage()
    {
        HistoryPreviewImage.Source = null;
        HistoryPreviewScrollViewer.Visibility = Visibility.Collapsed;
        HistoryPreviewUnavailableTextBlock.Text = string.Empty;
        HistoryPreviewUnavailableTextBlock.Visibility = Visibility.Collapsed;
    }

    private static HistoryPreviewData BuildHistoryPreviewData(CaptureTranslationRecord record)
    {
        var timestamp = record.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var workflow = FormatWorkflow(record.WorkflowType);
        var meta = $"时间：{timestamp}\n类型：{workflow}\n路径：{FormatSummaryValue(record.ImagePath)}";

        return record.WorkflowType switch
        {
            "ocr" => new HistoryPreviewData(
                "OCR 识别记录",
                meta,
                string.IsNullOrWhiteSpace(record.OcrError) ? "OCR 识别已完成。" : $"OCR 失败：{record.OcrError}",
                "OCR 文本",
                PickValue(record.SourceText, record.OcrError),
                "调试信息",
                PickValue(record.OcrDebugInfo, "当前没有额外调试信息。")),
            "ocr-translate" => new HistoryPreviewData(
                "OCR 并翻译记录",
                meta,
                BuildHistoryTranslateStatus(record),
                "原文",
                PickValue(record.SourceText, record.OcrError),
                "译文",
                PickValue(record.TranslatedText, record.TranslationError)),
            "qr" => new HistoryPreviewData(
                "二维码识别记录",
                meta,
                string.IsNullOrWhiteSpace(record.OcrError) ? "二维码识别已完成。" : $"二维码识别失败：{record.OcrError}",
                "二维码内容",
                PickValue(record.QrCodeText, record.OcrError),
                "补充信息",
                "这条记录来自二维码识别流程。"),
            "pin" => new HistoryPreviewData(
                "固定到屏幕记录",
                meta,
                "这条记录表示该截图曾被固定到屏幕。",
                "截图路径",
                PickValue(record.ImagePath, "没有可用的截图路径。"),
                "补充信息",
                "固定到屏幕不会额外产生 OCR 或翻译结果。"),
            _ => new HistoryPreviewData(
                "历史记录",
                meta,
                "该记录类型暂未定义专用预览结构。",
                "主要内容",
                PickMainHistoryContent(record),
                "补充信息",
                PickValue(record.OcrDebugInfo, "暂无补充信息。"))
        };
    }

    private static string PickValue(string? value, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "暂无内容。" : fallback;
    }

    private static string PickMainHistoryContent(CaptureTranslationRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.QrCodeText))
        {
            return record.QrCodeText;
        }

        if (!string.IsNullOrWhiteSpace(record.TranslatedText))
        {
            return record.TranslatedText;
        }

        if (!string.IsNullOrWhiteSpace(record.SourceText))
        {
            return record.SourceText;
        }

        if (!string.IsNullOrWhiteSpace(record.OcrError))
        {
            return record.OcrError;
        }

        return PickValue(record.ImagePath, "暂无内容。");
    }

    private void SetTestButtonsEnabled(bool isEnabled)
    {
        TestOcrButton.IsEnabled = isEnabled;
        TestApiConnectionButton.IsEnabled = isEnabled;
        TestTranslationButton.IsEnabled = isEnabled;
    }

    private static bool IsModifierOnlyKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
    }

    private static string FormatHotkeyText(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatPrimaryKey(key));
        return string.Join("+", parts);
    }

    private static string FormatPrimaryKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString().ToUpperInvariant();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString();
        }

        return key.ToString();
    }

    private static string CreateOcrTestImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapCat", "settings-tests");
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"ocr-test-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");

        using var bitmap = new Bitmap(960, 260);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(DrawingColor.White);

        using var titleFont = CreateFont(30, DrawingFontStyle.Bold);
        using var bodyFont = CreateFont(22, DrawingFontStyle.Regular);

        graphics.DrawString("SnapCat OCR 识别测试 123", titleFont, DrawingBrushes.Black, 28, 34);
        graphics.DrawString("本地识别与接口翻译", bodyFont, DrawingBrushes.Black, 32, 104);
        graphics.DrawString("自由框选 screenshot translation", bodyFont, DrawingBrushes.Black, 32, 154);

        bitmap.Save(filePath, ImageFormat.Png);
        return filePath;
    }

    private static DrawingFont CreateFont(float size, DrawingFontStyle style)
    {
        try
        {
            return new DrawingFont("Microsoft YaHei UI", size, style);
        }
        catch
        {
            return new DrawingFont(DrawingFontFamily.GenericSansSerif, size, style);
        }
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temporary cleanup failures.
        }
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

    private void ThemeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
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
        return value switch
        {
            "pin" => "固定到屏幕",
            "ocr" => "OCR 识别",
            "ocr-translate" => "OCR 并翻译",
            "qr" => "二维码识别",
            "save" => "保存截图",
            _ => "未知操作"
        };
    }

    private static string FormatSummaryValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未填写" : value;
    }

    private static string FormatRetentionDays(int days)
    {
        return days <= 0 ? "不自动清理" : $"{days} 天";
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.2.0";
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

    private void ApplyNavigationStyle(WpfButton button, bool isSelected)
    {
        button.Background = isSelected
            ? GetThemeBrush("Theme.Brush.Accent")
            : WpfBrushes.Transparent;
        button.BorderBrush = isSelected
            ? GetThemeBrush("Theme.Brush.AccentBorder")
            : WpfBrushes.Transparent;
        button.Foreground = isSelected
            ? GetThemeBrush("Theme.Brush.TextPrimary")
            : GetThemeBrush("Theme.Brush.TextPrimary");
        button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private System.Windows.Media.Brush GetThemeBrush(string resourceKey)
    {
        return TryFindResource(resourceKey) as System.Windows.Media.Brush ?? WpfBrushes.Transparent;
    }

    private sealed record HistoryListItem(CaptureTranslationRecord Record)
    {
        public string Summary
        {
            get
            {
                var timestamp = Record.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                var workflow = FormatWorkflow(Record.WorkflowType);
                var mainContent = PickMainContent(Record);
                return $"{timestamp} | {workflow} | {mainContent}";
            }
        }

        private static string PickMainContent(CaptureTranslationRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.QrCodeText))
            {
                return $"二维码：{TrimForList(record.QrCodeText)}";
            }

            if (!string.IsNullOrWhiteSpace(record.TranslatedText))
            {
                return $"译文：{TrimForList(record.TranslatedText)}";
            }

            if (!string.IsNullOrWhiteSpace(record.SourceText))
            {
                return $"文本：{TrimForList(record.SourceText)}";
            }

            if (!string.IsNullOrWhiteSpace(record.OcrError))
            {
                return $"错误：{TrimForList(record.OcrError)}";
            }

            return $"图片：{TrimForList(record.ImagePath)}";
        }

        private static string TrimForList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            var singleLine = value.ReplaceLineEndings(" ").Trim();
            const int maxLength = 42;
            return singleLine.Length <= maxLength
                ? singleLine
                : $"{singleLine[..maxLength]}...";
        }
    }

    private sealed record PinnedImageListItem(PinnedWindowSnapshot Snapshot)
    {
        public string Id => Snapshot.Id;

        public ImageSource? Thumbnail { get; } = LoadThumbnail(Snapshot.ImagePath);

        public string Title
        {
            get
            {
                var fileName = string.IsNullOrWhiteSpace(Snapshot.ImagePath)
                    ? "未知图片"
                    : Path.GetFileName(Snapshot.ImagePath);
                var groupName = string.IsNullOrWhiteSpace(Snapshot.GroupName) ? "未成组" : Snapshot.GroupName;
                return $"{fileName} · {groupName}";
            }
        }

        public string Summary
        {
            get
            {
                var groupName = string.IsNullOrWhiteSpace(Snapshot.GroupName) ? "未成组" : Snapshot.GroupName;
                var visibility = Snapshot.IsVisible ? "显示中" : "已隐藏";
                var fileName = string.IsNullOrWhiteSpace(Snapshot.ImagePath)
                    ? "未知图片"
                    : Path.GetFileName(Snapshot.ImagePath);
                return $"{groupName} | {visibility} | {Math.Round(Snapshot.Width)}x{Math.Round(Snapshot.Height)} | X:{Math.Round(Snapshot.Left)} Y:{Math.Round(Snapshot.Top)} | {fileName}";
            }
        }

        private static ImageSource? LoadThumbnail(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = 96;
                bitmap.UriSource = new Uri(imagePath);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed record DefaultCaptureListItem(string Path)
    {
        public ImageSource? Thumbnail { get; } = LoadThumbnail(Path);

        public string Title => System.IO.Path.GetFileName(Path);

        public string Summary
        {
            get
            {
                try
                {
                    var info = new FileInfo(Path);
                    return $"{info.LastWriteTime:yyyy-MM-dd HH:mm:ss} | {FormatFileSize(info.Length)} | {Path}";
                }
                catch
                {
                    return Path;
                }
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
            {
                return $"{bytes / 1024d / 1024d:0.##} MB";
            }

            return $"{Math.Max(1, bytes / 1024d):0.#} KB";
        }

        private static ImageSource? LoadThumbnail(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = 96;
                bitmap.UriSource = new Uri(imagePath);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed record CaptureActionSelectionResult(
        CaptureActionKind Action,
        Int32Rect CaptureRegion);

    private sealed record HistoryPreviewData(
        string Title,
        string Meta,
        string Status,
        string PrimaryHeader,
        string PrimaryText,
        string SecondaryHeader,
        string SecondaryText);

    private sealed record NavigationSectionMetadata(
        WpfButton Button,
        FrameworkElement Content,
        string Title,
        string Description);

    private enum MainSection
    {
        OcrSettings,
        TranslationSettings,
        History,
        ScreenshotManagement,
        PinnedImages,
        ExecuteActions,
        CaptureSettings,
        AppearanceSettings,
        TraySettings,
        Status
    }
}
