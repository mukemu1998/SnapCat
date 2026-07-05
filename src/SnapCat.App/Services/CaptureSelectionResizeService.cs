using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Services;

internal static class CaptureSelectionResizeService
{
    public static SelectionBoundsInput ApplyLockedAspectRatio(
        SelectionBoundsInput requestedBounds,
        int currentWidth,
        int currentHeight,
        double ratio,
        int minSelectionSize)
    {
        var width = requestedBounds.Width;
        var height = requestedBounds.Height;
        var widthChanged = width != currentWidth;
        var heightChanged = height != currentHeight;

        if (widthChanged && !heightChanged)
        {
            height = Math.Max(minSelectionSize, (int)Math.Round(width / ratio));
        }
        else if (!widthChanged && heightChanged)
        {
            width = Math.Max(minSelectionSize, (int)Math.Round(height * ratio));
        }
        else if (widthChanged)
        {
            height = Math.Max(minSelectionSize, (int)Math.Round(width / ratio));
        }

        return requestedBounds with
        {
            Width = Math.Max(minSelectionSize, width),
            Height = Math.Max(minSelectionSize, height)
        };
    }

    public static SelectionBoundsInput EnsureMinimumSize(
        SelectionBoundsInput bounds,
        int minSelectionSize)
    {
        return bounds with
        {
            Width = Math.Max(minSelectionSize, bounds.Width),
            Height = Math.Max(minSelectionSize, bounds.Height)
        };
    }

    public static WpfRect FitAspectRatioAroundCenter(
        WpfRect selectionRect,
        double allowedWidth,
        double allowedHeight,
        double minSelectionSize,
        double ratio)
    {
        var centerX = selectionRect.Left + (selectionRect.Width / 2);
        var centerY = selectionRect.Top + (selectionRect.Height / 2);

        var targetWidth = Math.Max(minSelectionSize, selectionRect.Width);
        var targetHeight = Math.Max(minSelectionSize, targetWidth / ratio);

        if (targetHeight > allowedHeight)
        {
            targetHeight = allowedHeight;
            targetWidth = Math.Max(minSelectionSize, targetHeight * ratio);
        }

        if (targetWidth > allowedWidth)
        {
            targetWidth = allowedWidth;
            targetHeight = Math.Max(minSelectionSize, targetWidth / ratio);
        }

        targetHeight = Math.Min(targetHeight, allowedHeight);
        targetWidth = Math.Min(targetWidth, allowedWidth);

        var left = centerX - (targetWidth / 2);
        var top = centerY - (targetHeight / 2);
        var right = left + targetWidth;
        var bottom = top + targetHeight;

        CaptureSelectionGeometryService.ConstrainToAllowedBounds(
            ref left,
            ref top,
            ref right,
            ref bottom,
            allowedWidth,
            allowedHeight,
            minSelectionSize);

        return new WpfRect(left, top, right - left, bottom - top);
    }

    public static WpfRect ApplyResizeDelta(
        WpfRect selectionRect,
        string resizeHandle,
        double horizontalChange,
        double verticalChange,
        double allowedWidth,
        double allowedHeight,
        double minSelectionSize,
        bool lockAspectRatio,
        double aspectRatio)
    {
        var left = selectionRect.Left;
        var top = selectionRect.Top;
        var right = selectionRect.Right;
        var bottom = selectionRect.Bottom;

        if (HasLeftHandle(resizeHandle))
        {
            left = CaptureSelectionGeometryService.Clamp(left + horizontalChange, 0, right - minSelectionSize);
        }

        if (HasRightHandle(resizeHandle))
        {
            right = CaptureSelectionGeometryService.Clamp(right + horizontalChange, left + minSelectionSize, allowedWidth);
        }

        if (HasTopHandle(resizeHandle))
        {
            top = CaptureSelectionGeometryService.Clamp(top + verticalChange, 0, bottom - minSelectionSize);
        }

        if (HasBottomHandle(resizeHandle))
        {
            bottom = CaptureSelectionGeometryService.Clamp(bottom + verticalChange, top + minSelectionSize, allowedHeight);
        }

        if (lockAspectRatio)
        {
            ApplyAspectRatioConstraint(
                resizeHandle,
                selectionRect,
                ref left,
                ref top,
                ref right,
                ref bottom,
                horizontalChange,
                verticalChange,
                allowedWidth,
                allowedHeight,
                minSelectionSize,
                aspectRatio);
        }

        CaptureSelectionGeometryService.ConstrainToAllowedBounds(
            ref left,
            ref top,
            ref right,
            ref bottom,
            allowedWidth,
            allowedHeight,
            minSelectionSize);

        left = Math.Round(left);
        top = Math.Round(top);
        right = Math.Round(right);
        bottom = Math.Round(bottom);

        return new WpfRect(left, top, right - left, bottom - top);
    }

    public static void ApplyAspectRatioConstraint(
        string resizeHandle,
        WpfRect originalRect,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom,
        double horizontalChange,
        double verticalChange,
        double allowedWidth,
        double allowedHeight,
        double minSelectionSize,
        double ratio)
    {
        if (ratio <= 0)
        {
            return;
        }

        var widthDriven = resizeHandle is "Left" or "Right"
            || ((HasLeftHandle(resizeHandle) || HasRightHandle(resizeHandle))
                && Math.Abs(horizontalChange) >= Math.Abs(verticalChange));

        if (widthDriven)
        {
            var newWidth = Math.Max(minSelectionSize, right - left);
            var newHeight = Math.Max(minSelectionSize, newWidth / ratio);

            if (HasTopHandle(resizeHandle))
            {
                top = bottom - newHeight;
            }
            else if (HasBottomHandle(resizeHandle))
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
            var newHeight = Math.Max(minSelectionSize, bottom - top);
            var newWidth = Math.Max(minSelectionSize, newHeight * ratio);

            if (HasLeftHandle(resizeHandle))
            {
                left = right - newWidth;
            }
            else if (HasRightHandle(resizeHandle))
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

        left = CaptureSelectionGeometryService.Clamp(left, 0, allowedWidth - minSelectionSize);
        top = CaptureSelectionGeometryService.Clamp(top, 0, allowedHeight - minSelectionSize);
        right = CaptureSelectionGeometryService.Clamp(right, left + minSelectionSize, allowedWidth);
        bottom = CaptureSelectionGeometryService.Clamp(bottom, top + minSelectionSize, allowedHeight);

        var constrainedWidth = right - left;
        var constrainedHeight = Math.Max(minSelectionSize, constrainedWidth / ratio);
        if (constrainedHeight > allowedHeight)
        {
            constrainedHeight = allowedHeight;
            constrainedWidth = Math.Max(minSelectionSize, constrainedHeight * ratio);
        }

        if (HasTopHandle(resizeHandle))
        {
            top = bottom - constrainedHeight;
        }
        else
        {
            bottom = top + constrainedHeight;
        }

        if (HasLeftHandle(resizeHandle))
        {
            left = right - constrainedWidth;
        }
        else if (HasRightHandle(resizeHandle))
        {
            right = left + constrainedWidth;
        }

        left = CaptureSelectionGeometryService.Clamp(left, 0, allowedWidth - constrainedWidth);
        top = CaptureSelectionGeometryService.Clamp(top, 0, allowedHeight - constrainedHeight);
        right = left + constrainedWidth;
        bottom = top + constrainedHeight;
    }

    private static bool HasLeftHandle(string resizeHandle)
    {
        return resizeHandle.Contains("Left", StringComparison.Ordinal);
    }

    private static bool HasRightHandle(string resizeHandle)
    {
        return resizeHandle.Contains("Right", StringComparison.Ordinal);
    }

    private static bool HasTopHandle(string resizeHandle)
    {
        return resizeHandle.Contains("Top", StringComparison.Ordinal);
    }

    private static bool HasBottomHandle(string resizeHandle)
    {
        return resizeHandle.Contains("Bottom", StringComparison.Ordinal);
    }
}
