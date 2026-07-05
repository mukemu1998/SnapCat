namespace SnapCat.App.Services;

internal static class SettingsValueParser
{
    public static int ParseRetentionDays(string? value, int fallback)
    {
        return int.TryParse(value?.Trim(), out var days)
            ? Math.Max(0, days)
            : Math.Max(0, fallback);
    }
}
