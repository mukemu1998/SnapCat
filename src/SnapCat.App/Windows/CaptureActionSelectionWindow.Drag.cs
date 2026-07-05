using System.Windows;
using System.Windows.Controls.Primitives;
using SnapCat.App.Services;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Windows;

public partial class CaptureActionSelectionWindow
{
    private void MoveThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _moveDragOriginRect = _selectionRect;
        _moveDragAccumulatedX = 0;
        _moveDragAccumulatedY = 0;
        _moveAxisLock = MoveAxisLock.None;
        BeginSelectionDrag(DragInteractionKind.Move);
    }

    private void MoveThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        _moveDragAccumulatedX += e.HorizontalChange;
        _moveDragAccumulatedY += e.VerticalChange;

        var horizontalChange = _moveDragAccumulatedX;
        var verticalChange = _moveDragAccumulatedY;
        var shiftPressed =
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift)
            == System.Windows.Input.ModifierKeys.Shift;

        if (shiftPressed)
        {
            if (_moveAxisLock == MoveAxisLock.None
                && (Math.Abs(horizontalChange) > 0.5 || Math.Abs(verticalChange) > 0.5))
            {
                _moveAxisLock = Math.Abs(horizontalChange) >= Math.Abs(verticalChange)
                    ? MoveAxisLock.Horizontal
                    : MoveAxisLock.Vertical;
            }

            if (_moveAxisLock == MoveAxisLock.Horizontal)
            {
                verticalChange = 0;
            }
            else if (_moveAxisLock == MoveAxisLock.Vertical)
            {
                horizontalChange = 0;
            }
        }
        else
        {
            _moveAxisLock = MoveAxisLock.None;
        }

        var movementBounds = GetMovementBounds(_selectionRect.Width, _selectionRect.Height);
        var newX = Math.Round(CaptureSelectionGeometryService.Clamp(_moveDragOriginRect.X + horizontalChange, movementBounds.Left, movementBounds.Right));
        var newY = Math.Round(CaptureSelectionGeometryService.Clamp(_moveDragOriginRect.Y + verticalChange, movementBounds.Top, movementBounds.Bottom));
        _selectionRect = new WpfRect(newX, newY, _moveDragOriginRect.Width, _moveDragOriginRect.Height);
        UpdateSelectionChrome();
    }

    private void MoveThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _moveDragOriginRect = _selectionRect;
        _moveDragAccumulatedX = 0;
        _moveDragAccumulatedY = 0;
        _moveAxisLock = MoveAxisLock.None;
        EndSelectionDrag();
    }

    private void ResizeThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginSelectionDrag(DragInteractionKind.Resize);
    }

    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag)
        {
            return;
        }

        _selectionRect = CaptureSelectionResizeService.ApplyResizeDelta(
            _selectionRect,
            tag,
            e.HorizontalChange,
            e.VerticalChange,
            RootCanvas.ActualWidth,
            RootCanvas.ActualHeight,
            MinSelectionSize,
            LockAspectRatioCheckBox.IsChecked == true,
            GetLockedAspectRatio(_selectionRect));
        UpdateSelectionChrome();
    }

    private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        EndSelectionDrag();
    }

    private void BeginSelectionDrag(DragInteractionKind interactionKind)
    {
        if (_isSelectionDragging)
        {
            return;
        }

        _isSelectionDragging = true;
        _dragInteractionKind = interactionKind;
        _lastAdjustmentPanelRefreshUtc = DateTime.MinValue;
        _cachedToolbarSize = MeasureToolbarSize();
    }

    private void EndSelectionDrag()
    {
        if (!_isSelectionDragging)
        {
            return;
        }

        _isSelectionDragging = false;
        _dragInteractionKind = DragInteractionKind.None;
        _lastAdjustmentPanelRefreshUtc = DateTime.MinValue;
        UpdateSelectionChrome(forceToolbarMeasure: true, forcePanelRefresh: true);
    }

    private WpfRect GetMovementBounds(double width, double height)
    {
        return CaptureSelectionGeometryService.GetMovementBounds(
            RootCanvas.ActualWidth,
            RootCanvas.ActualHeight,
            width,
            height);
    }

    private void ConstrainSelectionToAllowedBounds(ref double left, ref double top, ref double right, ref double bottom)
    {
        CaptureSelectionGeometryService.ConstrainToAllowedBounds(
            ref left,
            ref top,
            ref right,
            ref bottom,
            RootCanvas.ActualWidth,
            RootCanvas.ActualHeight,
            MinSelectionSize);
    }
}
