using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FormsScreen = System.Windows.Forms.Screen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace SnapCat.App.Windows;

public partial class CaptureActionSelectionWindow
{
    private void UpdateSelectionChrome(bool forceToolbarMeasure = false, bool forcePanelRefresh = false)
    {
        Canvas.SetLeft(SelectionOutline, _selectionRect.X);
        Canvas.SetTop(SelectionOutline, _selectionRect.Y);
        SelectionOutline.Width = _selectionRect.Width;
        SelectionOutline.Height = _selectionRect.Height;

        Canvas.SetLeft(MoveThumb, _selectionRect.X);
        Canvas.SetTop(MoveThumb, _selectionRect.Y);
        MoveThumb.Width = _selectionRect.Width;
        MoveThumb.Height = _selectionRect.Height;

        PositionThumb(TopLeftThumb, _selectionRect.Left - HandleSize / 2, _selectionRect.Top - HandleSize / 2);
        PositionThumb(TopThumb, _selectionRect.Left + (_selectionRect.Width - HandleSize) / 2, _selectionRect.Top - HandleSize / 2);
        PositionThumb(TopRightThumb, _selectionRect.Right - HandleSize / 2, _selectionRect.Top - HandleSize / 2);
        PositionThumb(RightThumb, _selectionRect.Right - HandleSize / 2, _selectionRect.Top + (_selectionRect.Height - HandleSize) / 2);
        PositionThumb(BottomRightThumb, _selectionRect.Right - HandleSize / 2, _selectionRect.Bottom - HandleSize / 2);
        PositionThumb(BottomThumb, _selectionRect.Left + (_selectionRect.Width - HandleSize) / 2, _selectionRect.Bottom - HandleSize / 2);
        PositionThumb(BottomLeftThumb, _selectionRect.Left - HandleSize / 2, _selectionRect.Bottom - HandleSize / 2);
        PositionThumb(LeftThumb, _selectionRect.Left - HandleSize / 2, _selectionRect.Top + (_selectionRect.Height - HandleSize) / 2);

        if (_isSelectionDragging && _dragInteractionKind == DragInteractionKind.Move)
        {
            ToolbarHost.Visibility = Visibility.Hidden;
            MoveThumb.Cursor = System.Windows.Input.Cursors.SizeAll;
            return;
        }

        ToolbarHost.Visibility = Visibility.Visible;

        if (forceToolbarMeasure || (_cachedToolbarSize.Width <= 0 || _cachedToolbarSize.Height <= 0))
        {
            _cachedToolbarSize = MeasureToolbarSize();
        }

        var toolbarSize = _cachedToolbarSize;
        var toolbarPosition = CalculateToolbarPosition(toolbarSize.Width, toolbarSize.Height);

        Canvas.SetLeft(ToolbarHost, toolbarPosition.X - Left);
        Canvas.SetTop(ToolbarHost, toolbarPosition.Y - Top);
        MoveThumb.Cursor = System.Windows.Input.Cursors.SizeAll;
        UpdateSelectionAdjustmentPanel(forcePanelRefresh);
        UpdateApplyPreviousBoundsButtonState();
    }

    private WpfPoint CalculateToolbarPosition(double toolbarWidth, double toolbarHeight)
    {
        var workArea = GetCurrentScreenWorkAreaLocalBounds();
        var maxX = Math.Max(workArea.Left, workArea.Right - toolbarWidth);
        var maxY = Math.Max(workArea.Top, workArea.Bottom - toolbarHeight);

        var centeredX = Clamp(
            _selectionRect.X + (_selectionRect.Width - toolbarWidth) / 2,
            workArea.Left,
            maxX);

        var centeredY = Clamp(
            _selectionRect.Y + (_selectionRect.Height - toolbarHeight) / 2,
            workArea.Top,
            maxY);

        var candidates = new[]
        {
            new WpfPoint(centeredX, _selectionRect.Bottom + ToolbarGap),
            new WpfPoint(centeredX, _selectionRect.Y - toolbarHeight - ToolbarGap),
            new WpfPoint(_selectionRect.Right + ToolbarGap, centeredY),
            new WpfPoint(_selectionRect.X - toolbarWidth - ToolbarGap, centeredY)
        };

        foreach (var candidate in candidates)
        {
            if (candidate.X >= workArea.Left
                && candidate.Y >= workArea.Top
                && candidate.X + toolbarWidth <= workArea.Right
                && candidate.Y + toolbarHeight <= workArea.Bottom)
            {
                return new WpfPoint(Left + candidate.X, Top + candidate.Y);
            }
        }

        var fallbackX = Clamp(centeredX, workArea.Left, maxX);
        var fallbackY = _selectionRect.Bottom + ToolbarGap <= workArea.Bottom - toolbarHeight
            ? _selectionRect.Bottom + ToolbarGap
            : _selectionRect.Y - toolbarHeight - ToolbarGap;

        return new WpfPoint(
            Left + fallbackX,
            Top + Clamp(fallbackY, workArea.Top, maxY));
    }

    private WpfRect GetCurrentScreenWorkAreaLocalBounds()
    {
        var selectedRegion = BuildSelectedRegion();
        var selectionBounds = new Rectangle(
            selectedRegion.X,
            selectedRegion.Y,
            Math.Max(1, selectedRegion.Width),
            Math.Max(1, selectedRegion.Height));

        var screen = FormsScreen.FromRectangle(selectionBounds);
        var workArea = screen.WorkingArea;

        var topLeftDip = _fromDevice.Transform(new WpfPoint(workArea.Left, workArea.Top));
        var bottomRightDip = _fromDevice.Transform(new WpfPoint(workArea.Right, workArea.Bottom));

        return new WpfRect(
            topLeftDip.X - Left,
            topLeftDip.Y - Top,
            bottomRightDip.X - topLeftDip.X,
            bottomRightDip.Y - topLeftDip.Y);
    }

    private void UpdateSelectionAdjustmentPanel(bool forceRefresh = false)
    {
        if (_isApplyingBoundsInputs)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!forceRefresh && _isSelectionDragging && (now - _lastAdjustmentPanelRefreshUtc).TotalMilliseconds < DragPanelRefreshIntervalMs)
        {
            return;
        }

        var selectedRegion = BuildSelectedRegion();
        var screen = FormsScreen.FromRectangle(new Rectangle(
            selectedRegion.X,
            selectedRegion.Y,
            Math.Max(1, selectedRegion.Width),
            Math.Max(1, selectedRegion.Height)));
        var workArea = screen.WorkingArea;
        var relativeX = selectedRegion.X - workArea.Left;
        var relativeY = selectedRegion.Y - workArea.Top;
        var ratio = selectedRegion.Height <= 0
            ? 0d
            : (double)selectedRegion.Width / selectedRegion.Height;

        SelectionInfoTextBlock.Text =
            $"{GetScreenDisplayLabel(screen)} | 绝对 X:{selectedRegion.X} Y:{selectedRegion.Y} W:{selectedRegion.Width} H:{selectedRegion.Height}";
        SelectionScreenInfoTextBlock.Text =
            $"屏幕内 X:{relativeX} Y:{relativeY} | 比例 {ratio:0.0000}";

        _isApplyingBoundsInputs = true;
        AbsoluteXTextBox.Text = selectedRegion.X.ToString(CultureInfo.InvariantCulture);
        AbsoluteYTextBox.Text = selectedRegion.Y.ToString(CultureInfo.InvariantCulture);
        WidthTextBox.Text = selectedRegion.Width.ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = selectedRegion.Height.ToString(CultureInfo.InvariantCulture);
        if (!ReferenceEquals(Keyboard.FocusedElement, AspectRatioTextBox))
        {
            AspectRatioTextBox.Text = ratio <= 0 ? "1.0000" : ratio.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        _isApplyingBoundsInputs = false;
        _lastAdjustmentPanelRefreshUtc = now;
    }

    private static void PositionThumb(FrameworkElement thumb, double x, double y)
    {
        Canvas.SetLeft(thumb, x);
        Canvas.SetTop(thumb, y);
    }

    private WpfSize MeasureToolbarSize()
    {
        ToolbarHost.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        return ToolbarHost.DesiredSize;
    }

    private static string GetScreenDisplayLabel(FormsScreen screen)
    {
        var index = Array.FindIndex(FormsScreen.AllScreens, candidate =>
            string.Equals(candidate.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase));
        var screenNumber = index >= 0 ? index + 1 : 1;
        return $"屏幕 {screenNumber} ({screen.DeviceName})";
    }
}
