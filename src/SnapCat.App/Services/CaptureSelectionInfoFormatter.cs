using System.Drawing;
using System.Globalization;
using System.Windows;
using FormsScreen = System.Windows.Forms.Screen;

namespace SnapCat.App.Services;

internal sealed record CaptureSelectionInfoText(
    string SelectionInfo,
    string ScreenInfo,
    string AspectRatioInput);

internal static class CaptureSelectionInfoFormatter
{
    public static CaptureSelectionInfoText Format(Int32Rect selectedRegion, FormsScreen screen)
    {
        var workArea = screen.WorkingArea;
        var relativeX = selectedRegion.X - workArea.Left;
        var relativeY = selectedRegion.Y - workArea.Top;
        var ratio = selectedRegion.Height <= 0
            ? 0d
            : (double)selectedRegion.Width / selectedRegion.Height;

        return new CaptureSelectionInfoText(
            $"{GetScreenDisplayLabel(screen)} | 绝对 X:{selectedRegion.X} Y:{selectedRegion.Y} W:{selectedRegion.Width} H:{selectedRegion.Height}",
            $"屏幕内 X:{relativeX} Y:{relativeY} | 比例 {ratio:0.0000}",
            ratio <= 0 ? "1.0000" : ratio.ToString("0.0000", CultureInfo.InvariantCulture));
    }

    public static string GetScreenDisplayLabel(FormsScreen screen)
    {
        var index = Array.FindIndex(FormsScreen.AllScreens, candidate =>
            string.Equals(candidate.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase));
        var screenNumber = index >= 0 ? index + 1 : 1;
        return $"屏幕 {screenNumber} ({screen.DeviceName})";
    }
}
