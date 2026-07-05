using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Services;

internal static class CaptureSelectionGeometryService
{
    public static WpfRect GetMovementBounds(
        double allowedWidth,
        double allowedHeight,
        double selectionWidth,
        double selectionHeight)
    {
        return new WpfRect(
            0,
            0,
            Math.Max(0, allowedWidth - selectionWidth),
            Math.Max(0, allowedHeight - selectionHeight));
    }

    public static void ConstrainToAllowedBounds(
        ref double left,
        ref double top,
        ref double right,
        ref double bottom,
        double allowedWidth,
        double allowedHeight,
        double minSelectionSize)
    {
        var allowedBounds = new WpfRect(0, 0, allowedWidth, allowedHeight);
        var width = Math.Max(minSelectionSize, right - left);
        var height = Math.Max(minSelectionSize, bottom - top);

        width = Math.Min(width, allowedBounds.Width);
        height = Math.Min(height, allowedBounds.Height);

        left = Clamp(left, allowedBounds.Left, allowedBounds.Right - width);
        top = Clamp(top, allowedBounds.Top, allowedBounds.Bottom - height);
        right = left + width;
        bottom = top + height;
    }

    public static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Max(min, Math.Min(max, value));
    }

}
