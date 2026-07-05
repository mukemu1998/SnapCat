using System.Drawing;
using System.Windows;
using DrawingPoint = System.Drawing.Point;
using FormsScreen = System.Windows.Forms.Screen;

namespace SnapCat.App.Services;

internal static class SelectionPreviewRegionService
{
    public static Int32Rect? ChooseAutomationCandidate(
        IReadOnlyList<Int32Rect> candidates,
        DrawingPoint screenPoint)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var screenBounds = FormsScreen.FromPoint(screenPoint).Bounds;
        var screenArea = Math.Max(1d, screenBounds.Width * screenBounds.Height);
        var usableCandidates = candidates
            .Where(candidate => candidate.Width * candidate.Height <= screenArea * 0.75d)
            .OrderBy(candidate => candidate.Width * candidate.Height)
            .ToList();

        if (usableCandidates.Count == 0)
        {
            return null;
        }

        var first = candidates[0];
        if (first.Width >= 36 && first.Height >= 18)
        {
            return first;
        }

        return usableCandidates.FirstOrDefault(candidate => candidate.Width >= 36 && candidate.Height >= 18, first);
    }

    public static Int32Rect? NormalizeCandidateRect(
        double left,
        double top,
        double width,
        double height,
        DrawingPoint screenPoint,
        Rectangle virtualScreenBounds)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(width) || double.IsNaN(height)
            || width < 10 || height < 10)
        {
            return null;
        }

        var rect = new Int32Rect(
            (int)Math.Round(left),
            (int)Math.Round(top),
            (int)Math.Round(width),
            (int)Math.Round(height));

        if (!Contains(rect, screenPoint)
            || rect.Width >= virtualScreenBounds.Width - 4
            || rect.Height >= virtualScreenBounds.Height - 4)
        {
            return null;
        }

        return ClipToBounds(rect, virtualScreenBounds);
    }

    public static Int32Rect? ClipToBounds(Int32Rect rect, Rectangle bounds)
    {
        var clippedLeft = Math.Max(rect.X, bounds.Left);
        var clippedTop = Math.Max(rect.Y, bounds.Top);
        var clippedRight = Math.Min(rect.X + rect.Width, bounds.Right);
        var clippedBottom = Math.Min(rect.Y + rect.Height, bounds.Bottom);
        var clippedWidth = clippedRight - clippedLeft;
        var clippedHeight = clippedBottom - clippedTop;

        return clippedWidth >= 10 && clippedHeight >= 10
            ? new Int32Rect(clippedLeft, clippedTop, clippedWidth, clippedHeight)
            : null;
    }

    public static bool Contains(Int32Rect rect, DrawingPoint point)
    {
        return point.X >= rect.X
            && point.Y >= rect.Y
            && point.X <= rect.X + rect.Width
            && point.Y <= rect.Y + rect.Height;
    }

    public static bool IsNearRectEdge(Int32Rect rect, DrawingPoint point, int threshold)
    {
        return Math.Abs(point.X - rect.X) <= threshold
            || Math.Abs(point.X - (rect.X + rect.Width)) <= threshold
            || Math.Abs(point.Y - rect.Y) <= threshold
            || Math.Abs(point.Y - (rect.Y + rect.Height)) <= threshold;
    }
}
