using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnapCat.App.Services;
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
        var localPosition = CaptureSelectionToolbarLayoutService.CalculateToolbarPosition(
            _selectionRect,
            workArea,
            toolbarWidth,
            toolbarHeight,
            ToolbarGap);

        return new WpfPoint(Left + localPosition.X, Top + localPosition.Y);
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
        var infoText = CaptureSelectionInfoFormatter.Format(selectedRegion, screen);

        SelectionInfoTextBlock.Text = infoText.SelectionInfo;
        SelectionScreenInfoTextBlock.Text = infoText.ScreenInfo;

        _isApplyingBoundsInputs = true;
        AbsoluteXTextBox.Text = selectedRegion.X.ToString(CultureInfo.InvariantCulture);
        AbsoluteYTextBox.Text = selectedRegion.Y.ToString(CultureInfo.InvariantCulture);
        WidthTextBox.Text = selectedRegion.Width.ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = selectedRegion.Height.ToString(CultureInfo.InvariantCulture);
        if (!ReferenceEquals(Keyboard.FocusedElement, AspectRatioTextBox))
        {
            AspectRatioTextBox.Text = infoText.AspectRatioInput;
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
}
