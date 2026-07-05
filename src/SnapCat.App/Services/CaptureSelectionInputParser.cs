using System.Globalization;

namespace SnapCat.App.Services;

internal readonly record struct SelectionBoundsInput(int X, int Y, int Width, int Height);

internal static class CaptureSelectionInputParser
{
    public static bool TryParseBounds(
        string? xValue,
        string? yValue,
        string? widthValue,
        string? heightValue,
        out SelectionBoundsInput bounds)
    {
        bounds = default;

        if (!int.TryParse(xValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(yValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(widthValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(heightValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
        {
            return false;
        }

        bounds = new SelectionBoundsInput(x, y, width, height);
        return true;
    }

    public static bool TryParseAspectRatio(string? value, out double ratio)
    {
        ratio = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var separatorIndex = normalized.IndexOfAny([':', '/', 'x', 'X']);
        if (separatorIndex > 0 && separatorIndex < normalized.Length - 1)
        {
            var leftPart = normalized[..separatorIndex];
            var rightPart = normalized[(separatorIndex + 1)..];

            if (double.TryParse(leftPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftValue)
                && double.TryParse(rightPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightValue)
                && leftValue > 0
                && rightValue > 0)
            {
                ratio = leftValue / rightValue;
                return true;
            }
        }

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var directRatio)
            && directRatio > 0)
        {
            ratio = directRatio;
            return true;
        }

        return false;
    }

    public static double GetAspectRatioOrFallback(string? value, double fallbackWidth, double fallbackHeight)
    {
        if (TryParseAspectRatio(value, out var ratio))
        {
            return ratio;
        }

        return fallbackHeight <= 0 ? 1d : fallbackWidth / fallbackHeight;
    }
}
