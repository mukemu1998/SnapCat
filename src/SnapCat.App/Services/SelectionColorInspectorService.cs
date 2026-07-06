using System.Drawing;
using WpfPoint = System.Windows.Point;

namespace SnapCat.App.Services;

internal static class SelectionColorInspectorService
{
    public static string FormatColor(Color color, bool useHex)
    {
        return useHex
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"{color.R}, {color.G}, {color.B}";
    }

    public static WpfPoint CalculatePanelPosition(
        WpfPoint localPoint,
        double panelWidth,
        double panelHeight,
        double viewportWidth,
        double viewportHeight,
        double offset,
        double margin)
    {
        var left = localPoint.X + offset;
        var top = localPoint.Y + offset;

        if (left + panelWidth > viewportWidth - margin)
        {
            left = localPoint.X - panelWidth - offset;
        }

        if (top + panelHeight > viewportHeight - margin)
        {
            top = localPoint.Y - panelHeight - offset;
        }

        return new WpfPoint(
            Math.Clamp(left, margin, Math.Max(margin, viewportWidth - panelWidth - margin)),
            Math.Clamp(top, margin, Math.Max(margin, viewportHeight - panelHeight - margin)));
    }
}
