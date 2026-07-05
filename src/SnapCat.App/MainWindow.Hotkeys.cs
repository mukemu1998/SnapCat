using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SnapCat.App;

public partial class MainWindow
{
    private void RegisterHotkeys()
    {
        if (!_hotkeyHostReady)
        {
            _hotkeyRegistrationResults = Array.Empty<HotkeyRegistrationResult>();
            return;
        }

        _hotkeyRegistrationResults = _app.GlobalHotkeyService.RegisterAll(CreateHotkeyRegistrationRequests());

        var failedCount = _hotkeyRegistrationResults.Count(static result => !result.IsRegistered);
        if (failedCount > 0)
        {
            StatusTextBlock.Text = $"有 {failedCount} 组快捷键注册失败，请到“执行操作与快捷键”或“系统快捷键”查看原因。";
        }
    }

    private IReadOnlyList<HotkeyRegistrationRequest> CreateHotkeyRegistrationRequests()
    {
        return
        [
            new("固定到屏幕", _settings.HotkeyCaptureAndPin, () => _ = StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndPin, returnToMainWindow: false)),
            new("自动翻译", _settings.HotkeyCaptureAndTranslate, () => _ = StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndTranslate, returnToMainWindow: false)),
            new("等待操作", _settings.HotkeyCaptureAndWaitForAction, () => _ = StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndWaitForAction, returnToMainWindow: false)),
            new("保存到默认位置", _settings.HotkeyCaptureAndSave, () => _ = StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndSave, returnToMainWindow: false)),
            new("显示全部贴图", _settings.HotkeyShowAllPinned, ShowAllPinnedImages),
            new("隐藏全部贴图", _settings.HotkeyHideAllPinned, HideAllPinnedImages),
            new("显示未成组贴图", _settings.HotkeyShowUngroupedPinned, ShowUngroupedPinnedImages),
            new("显示贴图组 1", _settings.HotkeyShowPinnedGroupOne, () => ShowPinnedGroup(PinnedWindowRegistryService.GroupOneName)),
            new("显示贴图组 2", _settings.HotkeyShowPinnedGroupTwo, () => ShowPinnedGroup(PinnedWindowRegistryService.GroupTwoName)),
            new("显示贴图组 3", _settings.HotkeyShowPinnedGroupThree, () => ShowPinnedGroup(PinnedWindowRegistryService.GroupThreeName)),
            new("打开主菜单", _settings.HotkeyShowMainWindow, ShowMainWindow),
            new("退出软件", _settings.HotkeyExitApplication, ExitApplication)
        ];
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
        ClearHotkeyTextBox(HotkeyCaptureAndPinTextBox);
    }

    private void ClearTranslateHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyCaptureAndTranslateTextBox);
    }

    private void ClearWaitHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyCaptureAndWaitTextBox);
    }

    private void ClearSaveHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyCaptureAndSaveTextBox);
    }

    private void ClearPinnedCloseShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(PinnedCloseShortcutTextBox);
    }

    private void ClearPinnedHideShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(PinnedHideShortcutTextBox);
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
        MarkSettingsDirty();
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
        MarkSettingsDirty();
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

        if (HotkeyTextFormatter.IsModifierOnlyKey(key))
        {
            _recordingHotkeyTextBox.Text = "请继续按下主键...";
            return;
        }

        var hotkeyText = HotkeyTextFormatter.Format(key, Keyboard.Modifiers);
        _recordingHotkeyTextBox.Text = hotkeyText;
        StatusTextBlock.Text = $"已录制“{_recordingHotkeyLabel}”快捷键：{hotkeyText}";
        ResetHotkeyRecordingState();
        ValidateHotkeyConflicts();
        MarkSettingsDirty();
    }

    private void ResetHotkeyRecordingState()
    {
        _recordingHotkeyTextBox = null;
        _recordingHotkeyLabel = string.Empty;
        _recordingOriginalValue = string.Empty;
    }

    private void ValidateHotkeyConflicts()
    {
        var summary = HotkeyValidationFormatter.Build(GetHotkeyValidationInputs(), _hotkeyRegistrationResults);
        HotkeyValidationTextBlock.Foreground = new SolidColorBrush(summary.HasIssue
            ? System.Windows.Media.Color.FromRgb(185, 28, 28)
            : System.Windows.Media.Color.FromRgb(22, 101, 52));
        HotkeyValidationTextBlock.Text = summary.Text;
    }

    private Dictionary<string, string> GetHotkeyValidationInputs()
    {
        return new Dictionary<string, string>
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
    }
}
