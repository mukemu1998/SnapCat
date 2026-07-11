using System.Windows.Input;
using SnapCat.Core.Services;

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

    public static string FormatText(string? text) => HotkeyTextNormalizer.Normalize(text);

    public static bool TryGetPrimaryKeyFromText(string text, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        if (normalized.Length == 1)
        {
            var character = normalized[0];
            if (character is >= 'A' and <= 'Z')
            {
                key = (Key)((int)Key.A + (character - 'A'));
                return true;
            }

            if (character is >= 'a' and <= 'z')
            {
                key = (Key)((int)Key.A + (character - 'a'));
                return true;
            }

            if (character is >= '0' and <= '9')
            {
                key = (Key)((int)Key.D0 + (character - '0'));
                return true;
            }
        }

        key = normalized switch
        {
            "`" or "~" => Key.Oem3,
            "-" or "_" => Key.OemMinus,
            "=" => Key.OemPlus,
            "[" or "{" => Key.OemOpenBrackets,
            "]" or "}" => Key.OemCloseBrackets,
            "\\" or "|" => Key.OemPipe,
            ";" or ":" => Key.OemSemicolon,
            "'" or "\"" => Key.OemQuotes,
            "," or "<" => Key.OemComma,
            "." or ">" => Key.OemPeriod,
            "/" or "?" => Key.OemQuestion,
            "Esc" => Key.Escape,
            "Space" or "空格" => Key.Space,
            "Backspace" or "退格" => Key.Back,
            "Enter" or "回车" => Key.Enter,
            "Tab" => Key.Tab,
            "Del" or "Delete" or "删除" => Key.Delete,
            "Ins" or "Insert" => Key.Insert,
            "←" => Key.Left,
            "→" => Key.Right,
            "↑" => Key.Up,
            "↓" => Key.Down,
            _ => key
        };

        if (key != Key.None)
        {
            return true;
        }

        if (normalized.StartsWith("Num", StringComparison.OrdinalIgnoreCase)
            && normalized.Length == 4
            && char.IsDigit(normalized[3]))
        {
            key = (Key)((int)Key.NumPad0 + (normalized[3] - '0'));
            return true;
        }

        return Enum.TryParse(normalized, true, out key);
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

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"Num{(int)(key - Key.NumPad0)}";
        }

        return key switch
        {
            Key.Oem3 => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.Escape => "Esc",
            Key.Space => "Space",
            Key.Back => "Backspace",
            Key.Delete => "Del",
            Key.Insert => "Ins",
            Key.Left => "←",
            Key.Right => "→",
            Key.Up => "↑",
            Key.Down => "↓",
            _ => key.ToString()
        };
    }

}
