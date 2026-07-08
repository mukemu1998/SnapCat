using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SnapCat.App.Services;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontFamily = System.Drawing.FontFamily;
using DrawingFontStyle = System.Drawing.FontStyle;
using Clipboard = System.Windows.Clipboard;

namespace SnapCat.App;

public partial class MainWindow
{
    private const int WindowsTextClipboardWatchSeconds = 90;
    private static readonly TimeSpan WindowsTextManualCopyAssistInitialDelay = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan WindowsTextManualCopyAssistInterval = TimeSpan.FromMilliseconds(850);
    private const int WindowsTextManualCopyAssistAttempts = 42;
    private DispatcherTimer? _windowsTextClipboardTimer;
    private CancellationTokenSource? _windowsTextManualCopyAssistCts;
    private DateTime _windowsTextClipboardWatchExpiresAt;
    private string _windowsTextInitialClipboardText = string.Empty;
    private uint _windowsTextInitialClipboardSequenceNumber;
    private bool _isHandlingWindowsTextClipboard;
    private bool _windowsTextClipboardShouldTranslate = true;
    private TranslationPopupWindow? _windowsTextTranslationPopupWindow;
    private Int32Rect? _windowsTextClipboardAnchorRegion;

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

    private async Task StartWindowsTextRecognitionBridgeAsync(
        bool translateAfterCopy,
        Int32Rect? autoSelectRegion = null)
    {
        try
        {
            var canAutoSelect = autoSelectRegion is not null
                && Services.WindowsTextExtractorLauncher.CanAutoSelectReliably(autoSelectRegion.Value);
            var autoSelectHint = autoSelectRegion is null
                ? "操作方式：使用系统文本提取框选文字区域，完成后复制识别文本。"
                : canAutoSelect
                    ? "操作方式：SnapCat 会尝试把当前选框自动交给系统文本提取；如果系统未接住，可手动补框选一次。"
                    : "操作方式：当前选区较小，已改为手动补框模式；请在系统文本提取中重新框选文字区域，完成后复制识别文本。";
            OcrTestResultTextBox.Text =
                "已触发 Win+Shift+T 文本提取。\n\n" +
                $"{autoSelectHint}\n\n" +
                (translateAfterCopy
                    ? $"SnapCat 会在接下来 {WindowsTextClipboardWatchSeconds} 秒内等待剪贴板文本；复制成功后会自动打开翻译浮窗。"
                    : $"SnapCat 会在接下来 {WindowsTextClipboardWatchSeconds} 秒内等待剪贴板文本；复制成功后会自动显示识别结果。");
            StatusTextBlock.Text = autoSelectRegion is null || !canAutoSelect
                ? (translateAfterCopy
                    ? "已触发 Win+Shift+T，请手动框选并复制识别文本后自动翻译。"
                    : "已触发 Win+Shift+T，请手动框选并复制识别文本。")
                : (translateAfterCopy
                    ? "已触发 Win+Shift+T，并尝试用当前选框自动提取后翻译。"
                    : "已触发 Win+Shift+T，并尝试用当前选框自动提取。");
            AppendOperationLog(StatusTextBlock.Text);

            _windowsTextClipboardAnchorRegion = autoSelectRegion;
            StartWindowsTextClipboardWatch(translateAfterCopy);
            await Services.WindowsTextExtractorLauncher.LaunchTextExtractorShortcutAsync(autoSelectRegion);
            if (autoSelectRegion is null || !canAutoSelect)
            {
                StartWindowsTextManualCopyAssist();
            }
        }
        catch (Exception ex)
        {
            StopWindowsTextClipboardWatch();
            OcrTestResultTextBox.Text =
                "触发 Win+Shift+T 文本提取失败。\n\n" +
                $"错误信息：{ex.Message}\n\n" +
                "可以检查系统是否启用了对应文本提取功能，例如 PowerToys Text Extractor 或系统同类功能。";
            StatusTextBlock.Text = $"触发 Win+Shift+T 文本提取失败：{ex.Message}";
            AppendOperationLog($"触发 Win+Shift+T 文本提取失败：{ex.Message}");
        }
    }

    private void StartWindowsTextClipboardWatch(bool translateAfterCopy)
    {
        StopWindowsTextClipboardWatch();

        _windowsTextClipboardShouldTranslate = translateAfterCopy;
        _windowsTextInitialClipboardText = TryGetClipboardText();
        _windowsTextInitialClipboardSequenceNumber = GetClipboardSequenceNumber();
        _windowsTextClipboardWatchExpiresAt = DateTime.Now.AddSeconds(WindowsTextClipboardWatchSeconds);
        _windowsTextClipboardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _windowsTextClipboardTimer.Tick += WindowsTextClipboardTimer_OnTick;
        _windowsTextClipboardTimer.Start();
    }

    private void StopWindowsTextClipboardWatch()
    {
        StopWindowsTextManualCopyAssist();

        if (_windowsTextClipboardTimer is null)
        {
            return;
        }

        _windowsTextClipboardTimer.Stop();
        _windowsTextClipboardTimer.Tick -= WindowsTextClipboardTimer_OnTick;
        _windowsTextClipboardTimer = null;
    }

    private void StartWindowsTextManualCopyAssist()
    {
        StopWindowsTextManualCopyAssist();

        _windowsTextManualCopyAssistCts = new CancellationTokenSource();
        var token = _windowsTextManualCopyAssistCts.Token;
        _ = RunWindowsTextManualCopyAssistAsync(token);
    }

    private void StopWindowsTextManualCopyAssist()
    {
        if (_windowsTextManualCopyAssistCts is null)
        {
            return;
        }

        _windowsTextManualCopyAssistCts.Cancel();
        _windowsTextManualCopyAssistCts.Dispose();
        _windowsTextManualCopyAssistCts = null;
    }

    private static async Task RunWindowsTextManualCopyAssistAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(WindowsTextManualCopyAssistInitialDelay, cancellationToken);
            for (var attempt = 0; attempt < WindowsTextManualCopyAssistAttempts; attempt++)
            {
                await Services.WindowsTextExtractorLauncher.TrySelectAllAndCopyAsync(cancellationToken);
                await Task.Delay(WindowsTextManualCopyAssistInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The clipboard watcher stops this helper as soon as text is received or the flow ends.
        }
    }

    private async void WindowsTextClipboardTimer_OnTick(object? sender, EventArgs e)
    {
        if (_isHandlingWindowsTextClipboard)
        {
            return;
        }

        if (DateTime.Now > _windowsTextClipboardWatchExpiresAt)
        {
            StopWindowsTextClipboardWatch();
            OcrTestResultTextBox.Text += "\n\n等待已结束：没有检测到 PowerToys 文本提取写入新的剪贴板文本。可能是自动拖选未被 PowerToys 接收，或 Text Extractor 没有复制识别内容。";
            StatusTextBlock.Text = "Win+Shift+T 文本提取未检测到剪贴板写入。";
            AppendOperationLog("Win+Shift+T 文本提取未检测到剪贴板写入。");
            return;
        }

        var text = TryGetClipboardText();
        var sequenceNumber = GetClipboardSequenceNumber();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (sequenceNumber == _windowsTextInitialClipboardSequenceNumber
            && string.Equals(text, _windowsTextInitialClipboardText, StringComparison.Ordinal))
        {
            return;
        }

        _isHandlingWindowsTextClipboard = true;
        StopWindowsTextClipboardWatch();

        try
        {
            if (_windowsTextClipboardShouldTranslate)
            {
                await ShowWindowsClipboardTranslationAsync(text.Trim());
            }
            else
            {
                ShowWindowsClipboardOcrResult(text.Trim());
            }
        }
        finally
        {
            _isHandlingWindowsTextClipboard = false;
        }
    }

    private void ShowWindowsClipboardOcrResult(string sourceText)
    {
        StatusTextBlock.Text = "Windows 识别文本已复制并接收。";
        OcrTestResultTextBox.Text =
            "已接收 Windows 识别文本，内容已由系统文本提取器复制到剪贴板。\n\n" +
            $"识别文本：\n{sourceText}";
        AppendOperationLog("Windows 识别文本已复制并接收。");
    }

    private async Task ShowWindowsClipboardTranslationAsync(string sourceText)
    {
        var settings = BuildCurrentSettings();
        var popupSettings = TranslationLanguageHelper.CloneSettings(settings);
        var popupWindow = _windowsTextTranslationPopupWindow;
        var anchorRegion = _windowsTextClipboardAnchorRegion;

        if (popupWindow is null)
        {
            popupWindow = new TranslationPopupWindow(
                "Windows 文本识别并翻译",
                "已接收 Windows 识别文本，正在翻译...",
                sourceText,
                string.Empty,
                popupSettings,
                anchorRegion,
                this,
                CreateWindowsTextRepeatCaptureAction());
            popupWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_windowsTextTranslationPopupWindow, popupWindow))
                {
                    _windowsTextTranslationPopupWindow = null;
                }
            };
            _windowsTextTranslationPopupWindow = popupWindow;
        }
        else
        {
            popupWindow.PrepareForReuse(
                "Windows 文本识别并翻译",
                popupSettings,
                anchorRegion,
                CreateWindowsTextRepeatCaptureAction(),
                preserveCurrentPosition: true);
            popupWindow.UpdateRecognizedSource(sourceText, "已接收 Windows 识别文本，正在翻译...");
        }

        popupWindow.ShowAboveSelectionOverlay();

        try
        {
            var effectiveSettings = TranslationLanguageHelper.BuildSettingsForTranslation(
                popupWindow.CreateCurrentSettingsSnapshot(),
                sourceText);
            var result = await _app.TranslationService.TranslateAsync(sourceText, effectiveSettings);
            if (result.Success)
            {
                popupWindow.UpdateTranslationResult(result.Text, "Windows 识别文本已翻译完成。");
                StatusTextBlock.Text = "Windows 识别文本已自动送入翻译浮窗。";
                OcrTestResultTextBox.Text =
                    "已接收 Windows 识别文本并完成翻译。\n\n" +
                    $"识别文本：\n{sourceText}\n\n" +
                    $"译文：\n{result.Text}";
                AppendOperationLog("Windows 识别文本已自动翻译。");
            }
            else
            {
                popupWindow.UpdateFailure($"翻译失败：{result.ErrorMessage}", result.ErrorMessage);
                StatusTextBlock.Text = $"Windows 识别文本翻译失败：{result.ErrorMessage}";
                OcrTestResultTextBox.Text =
                    "已接收 Windows 识别文本，但翻译失败。\n\n" +
                    $"识别文本：\n{sourceText}\n\n" +
                    $"错误信息：\n{result.ErrorMessage}";
                AppendOperationLog($"Windows 识别文本翻译失败：{result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            popupWindow.UpdateFailure($"翻译失败：{ex.Message}", ex.Message);
            StatusTextBlock.Text = $"Windows 识别文本翻译失败：{ex.Message}";
            OcrTestResultTextBox.Text =
                "已接收 Windows 识别文本，但翻译过程失败。\n\n" +
                $"识别文本：\n{sourceText}\n\n" +
                $"错误信息：\n{ex.Message}";
            AppendOperationLog($"Windows 识别文本翻译失败：{ex.Message}");
        }
    }

    private static string TryGetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText()
                ? Clipboard.GetText()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private Func<Task> CreateWindowsTextRepeatCaptureAction()
    {
        return () => StartWindowsTextRecognitionBridgeAsync(translateAfterCopy: true);
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
}
