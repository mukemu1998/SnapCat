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
        var selectionRect = SnapLocalRect(_selectionRect);
        _selectionRect = selectionRect;

        SetCanvasBounds(SelectionOutline, selectionRect);

        SetCanvasBounds(MoveThumb, selectionRect);

        PositionThumb(TopLeftThumb, selectionRect.Left - HandleSize / 2, selectionRect.Top - HandleSize / 2);
        PositionThumb(TopThumb, selectionRect.Left + (selectionRect.Width - HandleSize) / 2, selectionRect.Top - HandleSize / 2);
        PositionThumb(TopRightThumb, selectionRect.Right - HandleSize / 2, selectionRect.Top - HandleSize / 2);
        PositionThumb(RightThumb, selectionRect.Right - HandleSize / 2, selectionRect.Top + (selectionRect.Height - HandleSize) / 2);
        PositionThumb(BottomRightThumb, selectionRect.Right - HandleSize / 2, selectionRect.Bottom - HandleSize / 2);
        PositionThumb(BottomThumb, selectionRect.Left + (selectionRect.Width - HandleSize) / 2, selectionRect.Bottom - HandleSize / 2);
        PositionThumb(BottomLeftThumb, selectionRect.Left - HandleSize / 2, selectionRect.Bottom - HandleSize / 2);
        PositionThumb(LeftThumb, selectionRect.Left - HandleSize / 2, selectionRect.Top + (selectionRect.Height - HandleSize) / 2);

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
        var snappedToolbarPosition = SnapAbsolutePoint(toolbarPosition);

        Canvas.SetLeft(ToolbarHost, snappedToolbarPosition.X - Left);
        Canvas.SetTop(ToolbarHost, snappedToolbarPosition.Y - Top);
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

    private void PositionThumb(FrameworkElement thumb, double x, double y)
    {
        var point = SnapLocalPoint(new WpfPoint(x, y));
        Canvas.SetLeft(thumb, point.X);
        Canvas.SetTop(thumb, point.Y);
    }

    private WpfSize MeasureToolbarSize()
    {
        ToolbarHost.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var desiredSize = ToolbarHost.DesiredSize;
        return new WpfSize(
            SnapHorizontalLength(desiredSize.Width),
            SnapVerticalLength(desiredSize.Height));
    }

    private void SetCanvasBounds(FrameworkElement element, WpfRect rect)
    {
        Canvas.SetLeft(element, rect.X);
        Canvas.SetTop(element, rect.Y);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }

    private WpfRect SnapLocalRect(WpfRect rect)
    {
        var topLeft = SnapLocalPoint(new WpfPoint(rect.Left, rect.Top));
        var bottomRight = SnapLocalPoint(new WpfPoint(rect.Right, rect.Bottom));
        return new WpfRect(
            topLeft.X,
            topLeft.Y,
            Math.Max(0, bottomRight.X - topLeft.X),
            Math.Max(0, bottomRight.Y - topLeft.Y));
    }

    private WpfPoint SnapLocalPoint(WpfPoint localPoint)
    {
        var absolutePoint = new WpfPoint(Left + localPoint.X, Top + localPoint.Y);
        var snappedAbsolutePoint = SnapAbsolutePoint(absolutePoint);
        return new WpfPoint(snappedAbsolutePoint.X - Left, snappedAbsolutePoint.Y - Top);
    }

    private WpfPoint SnapAbsolutePoint(WpfPoint absolutePoint)
    {
        var devicePoint = _toDevice.Transform(absolutePoint);
        return _fromDevice.Transform(new WpfPoint(
            Math.Round(devicePoint.X),
            Math.Round(devicePoint.Y)));
    }

    private double SnapHorizontalLength(double value)
    {
        var deviceLength = _toDevice.Transform(new WpfPoint(value, 0)).X
            - _toDevice.Transform(new WpfPoint(0, 0)).X;
        var snappedEnd = _fromDevice.Transform(new WpfPoint(Math.Round(deviceLength), 0));
        return Math.Max(0, snappedEnd.X);
    }

    private double SnapVerticalLength(double value)
    {
        var deviceLength = _toDevice.Transform(new WpfPoint(0, value)).Y
            - _toDevice.Transform(new WpfPoint(0, 0)).Y;
        var snappedEnd = _fromDevice.Transform(new WpfPoint(0, Math.Round(deviceLength)));
        return Math.Max(0, snappedEnd.Y);
    }
}
