using System.Windows;
using System.Windows.Controls.Primitives;
using WpfPoint = System.Windows.Point;
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
        var newX = Math.Round(Clamp(_moveDragOriginRect.X + horizontalChange, movementBounds.Left, movementBounds.Right));
        var newY = Math.Round(Clamp(_moveDragOriginRect.Y + verticalChange, movementBounds.Top, movementBounds.Bottom));
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

        var left = _selectionRect.Left;
        var top = _selectionRect.Top;
        var right = _selectionRect.Right;
        var bottom = _selectionRect.Bottom;
        var originalRect = _selectionRect;

        switch (tag)
        {
            case "Left":
            case "TopLeft":
            case "BottomLeft":
                left = Clamp(left + e.HorizontalChange, 0, right - MinSelectionSize);
                break;
        }

        switch (tag)
        {
            case "Right":
            case "TopRight":
            case "BottomRight":
                right = Clamp(right + e.HorizontalChange, left + MinSelectionSize, RootCanvas.ActualWidth);
                break;
        }

        switch (tag)
        {
            case "Top":
            case "TopLeft":
            case "TopRight":
                top = Clamp(top + e.VerticalChange, 0, bottom - MinSelectionSize);
                break;
        }

        switch (tag)
        {
            case "Bottom":
            case "BottomLeft":
            case "BottomRight":
                bottom = Clamp(bottom + e.VerticalChange, top + MinSelectionSize, RootCanvas.ActualHeight);
                break;
        }

        if (LockAspectRatioCheckBox.IsChecked == true)
        {
            ApplyAspectRatioConstraint(tag, originalRect, ref left, ref top, ref right, ref bottom, e.HorizontalChange, e.VerticalChange);
        }

        ConstrainSelectionToAllowedBounds(ref left, ref top, ref right, ref bottom);
        left = Math.Round(left);
        top = Math.Round(top);
        right = Math.Round(right);
        bottom = Math.Round(bottom);

        _selectionRect = new WpfRect(new WpfPoint(left, top), new WpfPoint(right, bottom));
        UpdateSelectionChrome();
    }

    private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        EndSelectionDrag();
    }

    private void ApplyAspectRatioConstraint(
        string tag,
        WpfRect originalRect,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom,
        double horizontalChange,
        double verticalChange)
    {
        var ratio = GetLockedAspectRatio(originalRect);
        if (ratio <= 0)
        {
            return;
        }

        var widthDriven = tag is "Left" or "Right"
            || ((tag.Contains("Left", StringComparison.Ordinal) || tag.Contains("Right", StringComparison.Ordinal))
                && Math.Abs(horizontalChange) >= Math.Abs(verticalChange));

        if (widthDriven)
        {
            var newWidth = Math.Max(MinSelectionSize, right - left);
            var newHeight = Math.Max(MinSelectionSize, newWidth / ratio);

            if (tag.Contains("Top", StringComparison.Ordinal))
            {
                top = bottom - newHeight;
            }
            else if (tag.Contains("Bottom", StringComparison.Ordinal))
            {
                bottom = top + newHeight;
            }
            else
            {
                var centerY = originalRect.Top + originalRect.Height / 2;
                top = centerY - newHeight / 2;
                bottom = centerY + newHeight / 2;
            }
        }
        else
        {
            var newHeight = Math.Max(MinSelectionSize, bottom - top);
            var newWidth = Math.Max(MinSelectionSize, newHeight * ratio);

            if (tag.Contains("Left", StringComparison.Ordinal))
            {
                left = right - newWidth;
            }
            else if (tag.Contains("Right", StringComparison.Ordinal))
            {
                right = left + newWidth;
            }
            else
            {
                var centerX = originalRect.Left + originalRect.Width / 2;
                left = centerX - newWidth / 2;
                right = centerX + newWidth / 2;
            }
        }

        left = Clamp(left, 0, RootCanvas.ActualWidth - MinSelectionSize);
        top = Clamp(top, 0, RootCanvas.ActualHeight - MinSelectionSize);
        right = Clamp(right, left + MinSelectionSize, RootCanvas.ActualWidth);
        bottom = Clamp(bottom, top + MinSelectionSize, RootCanvas.ActualHeight);

        var constrainedWidth = right - left;
        var constrainedHeight = Math.Max(MinSelectionSize, constrainedWidth / ratio);
        if (constrainedHeight > RootCanvas.ActualHeight)
        {
            constrainedHeight = RootCanvas.ActualHeight;
            constrainedWidth = Math.Max(MinSelectionSize, constrainedHeight * ratio);
        }

        if (tag.Contains("Top", StringComparison.Ordinal))
        {
            top = bottom - constrainedHeight;
        }
        else
        {
            bottom = top + constrainedHeight;
        }

        if (tag.Contains("Left", StringComparison.Ordinal))
        {
            left = right - constrainedWidth;
        }
        else if (tag.Contains("Right", StringComparison.Ordinal))
        {
            right = left + constrainedWidth;
        }

        left = Clamp(left, 0, RootCanvas.ActualWidth - constrainedWidth);
        top = Clamp(top, 0, RootCanvas.ActualHeight - constrainedHeight);
        right = left + constrainedWidth;
        bottom = top + constrainedHeight;
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
        var allowedBounds = new WpfRect(0, 0, RootCanvas.ActualWidth, RootCanvas.ActualHeight);
        return new WpfRect(
            allowedBounds.Left,
            allowedBounds.Top,
            Math.Max(0, allowedBounds.Width - width),
            Math.Max(0, allowedBounds.Height - height));
    }

    private void ConstrainSelectionToAllowedBounds(ref double left, ref double top, ref double right, ref double bottom)
    {
        var allowedBounds = new WpfRect(0, 0, RootCanvas.ActualWidth, RootCanvas.ActualHeight);
        var width = Math.Max(MinSelectionSize, right - left);
        var height = Math.Max(MinSelectionSize, bottom - top);

        width = Math.Min(width, allowedBounds.Width);
        height = Math.Min(height, allowedBounds.Height);

        left = Clamp(left, allowedBounds.Left, allowedBounds.Right - width);
        top = Clamp(top, allowedBounds.Top, allowedBounds.Bottom - height);
        right = left + width;
        bottom = top + height;
    }
}
