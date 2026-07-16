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

        var failures = _hotkeyRegistrationResults
            .Where(static result => !result.IsRegistered)
            .ToList();
        if (failures.Count == 1)
        {
            var failure = failures[0];
            StatusTextBlock.Text = $"快捷键“{failure.Label}”（{HotkeyTextFormatter.FormatText(failure.HotkeyText)}）注册失败：{failure.Message}。请重新录制或关闭占用它的程序。";
        }
        else if (failures.Count > 1)
        {
            var labels = string.Join("、", failures.Select(static failure => failure.Label));
            StatusTextBlock.Text = $"{failures.Count} 组快捷键注册失败：{labels}。请到对应快捷键设置中查看并重新录制。";
        }
    }

    private IReadOnlyList<HotkeyRegistrationRequest> CreateHotkeyRegistrationRequests()
    {
        return
        [
            new("固定到屏幕", _settings.HotkeyCaptureAndPin, () => _ = StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndPin)),
            new("OCR 识别", _settings.HotkeyCaptureAndOcr, () => _ = StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndOcr)),
            new("自动翻译", _settings.HotkeyCaptureAndTranslate, () => _ = StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndTranslate)),
            new("等待操作", _settings.HotkeyCaptureAndWaitForAction, () => _ = StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndWaitForAction)),
            new("保存到默认位置", _settings.HotkeyCaptureAndSave, () => _ = StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndSave)),
            new("复制截图", _settings.HotkeyCaptureAndCopy, () => _ = StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndCopy)),
            new("框选标注", _settings.HotkeyCaptureAndAnnotate, () => _ = StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndAnnotate)),
            new("图片提示词分析", _settings.HotkeyCaptureAndVisualPrompt, () => _ = StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndVisualPrompt)),
            new("全屏画布编辑", _settings.HotkeyFullScreenCanvasEdit, () => _ = StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind.FullScreenCanvasEdit)),
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

    private Task StartHotkeyCaptureWorkflowAsync(CaptureWorkflowKind workflow)
    {
        var mainWindowIsOpen = IsVisible && WindowState != WindowState.Minimized;
        return StartCaptureWorkflowAsync(
            workflow,
            returnToMainWindow: false,
            hideMainWindowForCapture: !mainWindowIsOpen);
    }

    private void RecordPinHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndPinTextBox, "固定到屏幕");
    }

    private void RecordTranslateHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndTranslateTextBox, "自动翻译");
    }

    private void RecordOcrHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndOcrTextBox, "OCR 识别");
    }

    private void RecordWaitHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndWaitTextBox, "等待操作");
    }

    private void RecordSaveHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndSaveTextBox, "保存截图");
    }

    private void RecordCopyHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndCopyTextBox, "复制截图");
    }

    private void RecordFullScreenCanvasHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyFullScreenCanvasTextBox, "全屏画布编辑");
    }

    private void RecordAnnotateHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndAnnotateTextBox, "框选标注");
    }

    private void RecordVisualPromptHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        BeginHotkeyRecording(HotkeyCaptureAndVisualPromptTextBox, "图片提示词分析");
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

    private void ClearOcrHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyCaptureAndOcrTextBox);
    }

    private void ClearWaitHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyCaptureAndWaitTextBox);
    }

    private void ClearSaveHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyCaptureAndSaveTextBox);
    }

    private void ClearCopyHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyCaptureAndCopyTextBox);
    }

    private void ClearFullScreenCanvasHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyFullScreenCanvasTextBox);
    }

    private void ClearAnnotateHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyCaptureAndAnnotateTextBox);
    }

    private void ClearVisualPromptHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearHotkeyTextBox(HotkeyCaptureAndVisualPromptTextBox);
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
        HotkeyCaptureAndPinTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyCaptureAndPin);
        HotkeyCaptureAndOcrTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyCaptureAndOcr);
        HotkeyCaptureAndTranslateTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyCaptureAndTranslate);
        HotkeyCaptureAndWaitTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyCaptureAndWaitForAction);
        HotkeyCaptureAndSaveTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyCaptureAndSave);
        HotkeyCaptureAndCopyTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyCaptureAndCopy);
        HotkeyCaptureAndAnnotateTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyCaptureAndAnnotate);
        HotkeyCaptureAndVisualPromptTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyCaptureAndVisualPrompt);
        HotkeyFullScreenCanvasTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyFullScreenCanvasEdit);
        PinnedCloseShortcutTextBox.Text = HotkeyTextFormatter.FormatText(defaults.PinnedCloseShortcut);
        PinnedHideShortcutTextBox.Text = HotkeyTextFormatter.FormatText(defaults.PinnedHideShortcut);
        HotkeyShowAllPinnedTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyShowAllPinned);
        HotkeyHideAllPinnedTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyHideAllPinned);
        HotkeyShowUngroupedPinnedTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyShowUngroupedPinned);
        HotkeyShowPinnedGroupOneTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyShowPinnedGroupOne);
        HotkeyShowPinnedGroupTwoTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyShowPinnedGroupTwo);
        HotkeyShowPinnedGroupThreeTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyShowPinnedGroupThree);
        HotkeyShowMainWindowTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyShowMainWindow);
        HotkeyExitApplicationTextBox.Text = HotkeyTextFormatter.FormatText(defaults.HotkeyExitApplication);
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
            ["OCR 识别"] = HotkeyCaptureAndOcrTextBox.Text.Trim(),
            ["自动翻译"] = HotkeyCaptureAndTranslateTextBox.Text.Trim(),
            ["等待操作"] = HotkeyCaptureAndWaitTextBox.Text.Trim(),
            ["保存截图"] = HotkeyCaptureAndSaveTextBox.Text.Trim(),
            ["复制截图"] = HotkeyCaptureAndCopyTextBox.Text.Trim(),
            ["框选标注"] = HotkeyCaptureAndAnnotateTextBox.Text.Trim(),
            ["图片提示词分析"] = HotkeyCaptureAndVisualPromptTextBox.Text.Trim(),
            ["全屏画布编辑"] = HotkeyFullScreenCanvasTextBox.Text.Trim(),
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
