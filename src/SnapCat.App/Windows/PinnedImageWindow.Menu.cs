using System.IO;
using System.Windows;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using Clipboard = System.Windows.Clipboard;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using TextCompositionEventArgs = System.Windows.Input.TextCompositionEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SnapCat.App.Windows;

public partial class PinnedImageWindow
{
    private void ContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        SetHoverOverlayVisible(true);
        CloseOtherPinnedMenuItem.IsEnabled = _app.PinnedWindowRegistryService.HasOtherWindows(this);
        ResetOrientationMenuItem.IsEnabled = _flipHorizontally || _flipVertically;
        UngroupedGroupMenuItem.IsChecked = string.IsNullOrWhiteSpace(GroupName);
        GroupOneMenuItem.IsChecked = string.Equals(GroupName, PinnedWindowRegistryService.GroupOneName, StringComparison.Ordinal);
        GroupTwoMenuItem.IsChecked = string.Equals(GroupName, PinnedWindowRegistryService.GroupTwoName, StringComparison.Ordinal);
        GroupThreeMenuItem.IsChecked = string.Equals(GroupName, PinnedWindowRegistryService.GroupThreeName, StringComparison.Ordinal);
        ApplyOcrMenuLabels();
    }

    private void ApplyOcrMenuLabels()
    {
        if (IsWindowsTextRecognitionEngine(_settings.OcrEngine))
        {
            OcrMenuItem.Header = "OCR 识别并自动复制";
            OcrTranslateMenuItem.Header = "OCR 识别并自动复制后翻译";
            return;
        }

        OcrMenuItem.Header = "OCR 文本识别";
        OcrTranslateMenuItem.Header = "OCR 文本识别并翻译";
    }

    private static bool IsWindowsTextRecognitionEngine(string? value)
    {
        return string.Equals(value, "windows-text-extractor", StringComparison.Ordinal)
            || string.Equals(value, "windows-snipping-clipboard", StringComparison.Ordinal);
    }

    private void RootBorder_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SetHoverOverlayVisible(true);
    }

    private void RootBorder_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!PinnedContextMenu.IsOpen)
        {
            SetHoverOverlayVisible(false);
        }
    }

    private void CopyImageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetImage(GetEffectiveBitmapSource());
    }

    private void DuplicatePinnedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CreateDuplicatePinnedWindow(DuplicateOffset, DuplicateOffset);
    }

    private void CopyImagePathMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_imagePath);
    }

    private void OpenImageLocationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = WindowsExplorerService.OpenFileOrContainingDirectory(_imagePath);
            if (result == ExplorerOpenResult.Missing)
            {
                WpfMessageBox.Show(this, "截图文件和目录都不存在。", "打开失败");
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"打开截图位置失败：{ex.Message}", "打开失败");
        }
    }

    private void FlipHorizontalMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _flipHorizontally = !_flipHorizontally;
        UpdateImageOrientation();
    }

    private void FlipVerticalMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _flipVertically = !_flipVertically;
        UpdateImageOrientation();
    }

    private void ResetOrientationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _flipHorizontally = false;
        _flipVertically = false;
        UpdateImageOrientation();
    }

    private void ArrayCountTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is WpfTextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void ArrayCountTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !PinnedArrayCommandParser.IsNumericInput(e.Text);
    }

    private void ArrayCountTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not WpfTextBox textBox)
        {
            return;
        }

        if (!PinnedArrayCommandParser.TryResolveDirection(textBox.Tag, out var direction))
        {
            e.Handled = true;
            return;
        }

        var tileCount = PinnedArrayCommandParser.NormalizeTileCount(textBox.Text);
        textBox.Text = tileCount.ToString();
        PinnedContextMenu.IsOpen = false;
        CreateArrayPinnedWindow(direction, tileCount);
        e.Handled = true;
    }

    private void GroupMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string groupName })
        {
            SetPinnedGroup(groupName);
        }
    }

    private void SetPinnedGroup(string groupName)
    {
        _app.PinnedWindowRegistryService.SetWindowGroup(this, groupName);
    }

    private void ZoomInMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyScaleDelta(ScaleStep);
    }

    private void ZoomOutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyScaleDelta(-ScaleStep);
    }

    private void ResetZoomMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ResetToOriginalScale(GetWindowCenter());
    }

    private async void OcrMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ExecutePinnedCaptureActionAsync(CaptureActionKind.OcrOnly);
    }

    private async void OcrTranslateMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ExecutePinnedCaptureActionAsync(CaptureActionKind.OcrAndTranslate, includeCaptureRegion: true);
    }

    private async void QrCodeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ExecutePinnedCaptureActionAsync(CaptureActionKind.QrCode);
    }

    private async void SaveMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ExecutePinnedCaptureActionAsync(CaptureActionKind.Save);
    }

    private async void SaveAsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await ExecutePinnedCaptureActionAsync(CaptureActionKind.SaveAs);
    }

    private Task ExecutePinnedCaptureActionAsync(CaptureActionKind actionKind, bool includeCaptureRegion = false)
    {
        return _app.CaptureActionService.ExecuteAsync(
            actionKind,
            CreateOperationImagePath(),
            _settings,
            this,
            includeCaptureRegion ? _captureRegion : null);
    }

    private void CloseOtherPinnedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.CloseOtherWindows(this);
    }

    private void CloseAllPinnedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.CloseAllWindows();
    }

    private void CloseMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HideCurrentMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        HideCurrentPinnedWindow();
    }

    private void HideCurrentButton_OnClick(object sender, RoutedEventArgs e)
    {
        HideCurrentPinnedWindow();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RootBorder_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || IsInteractiveOverlayElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        HideCurrentPinnedWindow();
        e.Handled = true;
    }
}
