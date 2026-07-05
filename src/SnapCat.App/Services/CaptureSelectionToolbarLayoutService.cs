using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Services;

internal static class CaptureSelectionToolbarLayoutService
{
    public static WpfPoint CalculateToolbarPosition(
        WpfRect selectionRect,
        WpfRect workArea,
        double toolbarWidth,
        double toolbarHeight,
        double toolbarGap)
    {
        var maxX = Math.Max(workArea.Left, workArea.Right - toolbarWidth);
        var maxY = Math.Max(workArea.Top, workArea.Bottom - toolbarHeight);

        var centeredX = CaptureSelectionGeometryService.Clamp(
            selectionRect.X + (selectionRect.Width - toolbarWidth) / 2,
            workArea.Left,
            maxX);

        var centeredY = CaptureSelectionGeometryService.Clamp(
            selectionRect.Y + (selectionRect.Height - toolbarHeight) / 2,
            workArea.Top,
            maxY);

        var candidates = new[]
        {
            new WpfPoint(centeredX, selectionRect.Bottom + toolbarGap),
            new WpfPoint(centeredX, selectionRect.Y - toolbarHeight - toolbarGap),
            new WpfPoint(selectionRect.Right + toolbarGap, centeredY),
            new WpfPoint(selectionRect.X - toolbarWidth - toolbarGap, centeredY)
        };

        foreach (var candidate in candidates)
        {
            if (candidate.X >= workArea.Left
                && candidate.Y >= workArea.Top
                && candidate.X + toolbarWidth <= workArea.Right
                && candidate.Y + toolbarHeight <= workArea.Bottom)
            {
                return candidate;
            }
        }

        var fallbackX = CaptureSelectionGeometryService.Clamp(centeredX, workArea.Left, maxX);
        var fallbackY = selectionRect.Bottom + toolbarGap <= workArea.Bottom - toolbarHeight
            ? selectionRect.Bottom + toolbarGap
            : selectionRect.Y - toolbarHeight - toolbarGap;

        return new WpfPoint(fallbackX, CaptureSelectionGeometryService.Clamp(fallbackY, workArea.Top, maxY));
    }
}
