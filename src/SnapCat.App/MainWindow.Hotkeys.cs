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
            HotkeyValidationTextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 101, 52));
            HotkeyValidationTextBlock.Text = hotkeys.Any(static pair => !string.IsNullOrWhiteSpace(pair.Value))
                ? "当前快捷键没有发现重复冲突。"
                : "当前没有设置可选快捷键，需要时可在上方录制。";
            return;
        }

        if (duplicates.Count == 0)
        {
            HotkeyValidationTextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28));
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

        HotkeyValidationTextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28));
        HotkeyValidationTextBlock.Text = string.Join(Environment.NewLine, messages);
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
}
