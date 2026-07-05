using System.Windows.Input;

namespace SnapCat.App.Services;

public static class HotkeyTextFormatter
{
    public static bool IsModifierOnlyKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatPrimaryKey(key));
        return string.Join("+", parts);
    }

    private static string FormatPrimaryKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString().ToUpperInvariant();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString();
        }

        return key.ToString();
    }
}
