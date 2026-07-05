using System.Windows;
using System.Windows.Input;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Windows;

public partial class CaptureActionSelectionWindow
{
    private void BoundsTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            if (ReferenceEquals(sender, AspectRatioTextBox))
            {
                ApplyAspectRatioFromInput();
                return;
            }

            ApplyBoundsFromInputs();
        }
    }

    private void AspectRatioTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ApplyAspectRatioFromInput();
    }

    private void AspectRatioPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || element.Tag is not string preset
            || !CaptureSelectionInputParser.TryParseAspectRatio(preset, out var ratio))
        {
            return;
        }

        AspectRatioTextBox.Text = preset;
        LockAspectRatioCheckBox.IsChecked = true;
        ApplyAspectRatioToSelection(ratio);
    }

    private void ApplyBoundsFromInputs()
    {
        if (!TryParseBoundsInputs(out var bounds))
        {
            return;
        }

        var current = BuildSelectedRegion();
        var ratio = GetLockedAspectRatio(new WpfRect(0, 0, current.Width, current.Height));
        var normalizedBounds = LockAspectRatioCheckBox.IsChecked == true
            ? CaptureSelectionResizeService.ApplyLockedAspectRatio(
                bounds,
                current.Width,
                current.Height,
                ratio,
                (int)MinSelectionSize)
            : CaptureSelectionResizeService.EnsureMinimumSize(bounds, (int)MinSelectionSize);

        var bottomRightX = normalizedBounds.X + normalizedBounds.Width;
        var bottomRightY = normalizedBounds.Y + normalizedBounds.Height;
        var topLeftDip = _fromDevice.Transform(new WpfPoint(normalizedBounds.X, normalizedBounds.Y));
        var bottomRightDip = _fromDevice.Transform(new WpfPoint(bottomRightX, bottomRightY));

        var localLeft = topLeftDip.X - Left;
        var localTop = topLeftDip.Y - Top;
        var localRight = bottomRightDip.X - Left;
        var localBottom = bottomRightDip.Y - Top;

        ConstrainSelectionToAllowedBounds(
            ref localLeft,
            ref localTop,
            ref localRight,
            ref localBottom);

        _selectionRect = new WpfRect(localLeft, localTop, localRight - localLeft, localBottom - localTop);
        UpdateSelectionChrome();
    }

    private void ApplyAspectRatioFromInput()
    {
        if (_isApplyingBoundsInputs)
        {
            return;
        }

        if (!CaptureSelectionInputParser.TryParseAspectRatio(AspectRatioTextBox.Text, out var ratio))
        {
            SelectionScreenInfoTextBlock.Text = "请输入有效比例，例如 1:1、4:3、16:9 或 1.7778。";
            return;
        }

        LockAspectRatioCheckBox.IsChecked = true;
        ApplyAspectRatioToSelection(ratio);
    }

    private void ApplyAspectRatioToSelection(double ratio)
    {
        if (ratio <= 0 || RootCanvas.ActualWidth <= 0 || RootCanvas.ActualHeight <= 0)
        {
            return;
        }

        _selectionRect = CaptureSelectionResizeService.FitAspectRatioAroundCenter(
            _selectionRect,
            RootCanvas.ActualWidth,
            RootCanvas.ActualHeight,
            MinSelectionSize,
            ratio);
        UpdateSelectionChrome(forcePanelRefresh: true);
    }

    private bool TryParseBoundsInputs(out SelectionBoundsInput bounds)
    {
        if (CaptureSelectionInputParser.TryParseBounds(
                AbsoluteXTextBox.Text,
                AbsoluteYTextBox.Text,
                WidthTextBox.Text,
                HeightTextBox.Text,
                out bounds))
        {
            return true;
        }

        SelectionScreenInfoTextBlock.Text = "请输入有效的整数坐标和尺寸后再应用。";
        return false;
    }

    private void ActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag)
        {
            SelectedAction = CaptureActionKind.Cancel;
            DialogResult = false;
            return;
        }

        SelectedAction = Enum.Parse<CaptureActionKind>(tag);
        if (SelectedAction == CaptureActionKind.Cancel)
        {
            DialogResult = false;
            return;
        }

        SelectedRegion = BuildSelectedRegion();
        s_lastSelectionRegion = SelectedRegion;
        DialogResult = true;
    }

    private void ApplyPreviousBoundsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (s_lastSelectionRegion is not { } previousRegion)
        {
            SelectionScreenInfoTextBlock.Text = "当前还没有可应用的上一次线框数据。";
            UpdateApplyPreviousBoundsButtonState();
            return;
        }

        ApplySelectionRegion(previousRegion);
    }

    private void ResetInitialBoundsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplySelectionRegion(_initialCaptureRegion);
        SelectionScreenInfoTextBlock.Text = "已恢复到本次等待模式的初始框选。";
    }

    private void UpdateApplyPreviousBoundsButtonState()
    {
        ApplyPreviousBoundsButton.IsEnabled = s_lastSelectionRegion.HasValue;
    }

    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            SelectedAction = CaptureActionKind.Cancel;
            DialogResult = false;
            return;
        }

        if (TryHandleArrowNudge(e))
        {
            e.Handled = true;
        }
    }

    private bool TryHandleArrowNudge(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return false;
        }

        var step = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control ? 10d : 1d;
        var resizeMode = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        switch (e.Key)
        {
            case Key.Left:
                ApplyKeyboardNudge(-step, 0, resizeMode);
                return true;
            case Key.Right:
                ApplyKeyboardNudge(step, 0, resizeMode);
                return true;
            case Key.Up:
                ApplyKeyboardNudge(0, -step, resizeMode);
                return true;
            case Key.Down:
                ApplyKeyboardNudge(0, step, resizeMode);
                return true;
            default:
                return false;
        }
    }

    private void ApplyKeyboardNudge(double horizontalDelta, double verticalDelta, bool resizeMode)
    {
        var left = _selectionRect.Left;
        var top = _selectionRect.Top;
        var right = _selectionRect.Right;
        var bottom = _selectionRect.Bottom;
        var originalRect = _selectionRect;

        if (resizeMode)
        {
            if (horizontalDelta != 0)
            {
                right = CaptureSelectionGeometryService.Clamp(right + horizontalDelta, left + MinSelectionSize, RootCanvas.ActualWidth);
            }

            if (verticalDelta != 0)
            {
                bottom = CaptureSelectionGeometryService.Clamp(bottom + verticalDelta, top + MinSelectionSize, RootCanvas.ActualHeight);
            }

            if (LockAspectRatioCheckBox.IsChecked == true)
            {
                var tag = horizontalDelta != 0 && verticalDelta != 0
                    ? "BottomRight"
                    : horizontalDelta != 0
                        ? "Right"
                        : "Bottom";
                CaptureSelectionResizeService.ApplyAspectRatioConstraint(
                    tag,
                    originalRect,
                    ref left,
                    ref top,
                    ref right,
                    ref bottom,
                    horizontalDelta,
                    verticalDelta,
                    RootCanvas.ActualWidth,
                    RootCanvas.ActualHeight,
                    MinSelectionSize,
                    GetLockedAspectRatio(originalRect));
            }

            ConstrainSelectionToAllowedBounds(ref left, ref top, ref right, ref bottom);
            _selectionRect = new WpfRect(left, top, right - left, bottom - top);
        }
        else
        {
            var movementBounds = GetMovementBounds(_selectionRect.Width, _selectionRect.Height);
            var newX = CaptureSelectionGeometryService.Clamp(_selectionRect.X + horizontalDelta, movementBounds.Left, movementBounds.Right);
            var newY = CaptureSelectionGeometryService.Clamp(_selectionRect.Y + verticalDelta, movementBounds.Top, movementBounds.Bottom);
            _selectionRect = new WpfRect(newX, newY, _selectionRect.Width, _selectionRect.Height);
        }

        UpdateSelectionChrome(forcePanelRefresh: true);
    }

    private double GetLockedAspectRatio(WpfRect fallbackRect)
    {
        return CaptureSelectionInputParser.GetAspectRatioOrFallback(
            AspectRatioTextBox.Text,
            fallbackRect.Width,
            fallbackRect.Height);
    }
}
