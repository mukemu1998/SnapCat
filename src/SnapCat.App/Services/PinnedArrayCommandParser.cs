namespace SnapCat.App.Services;

internal static class PinnedArrayCommandParser
{
    public static int NormalizeTileCount(string text)
    {
        return int.TryParse(text, out var tileCount)
            ? Math.Clamp(tileCount, 1, 99)
            : 3;
    }

    public static bool IsNumericInput(string text)
    {
        return text.All(char.IsDigit);
    }

    public static bool TryResolveDirection(object? value, out PinnedArrayDirection direction)
    {
        if (value is string text
            && Enum.TryParse(text, ignoreCase: true, out direction))
        {
            return true;
        }

        direction = PinnedArrayDirection.Right;
        return false;
    }
}
