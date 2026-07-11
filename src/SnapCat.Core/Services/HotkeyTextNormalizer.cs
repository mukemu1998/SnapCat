namespace SnapCat.Core.Services;

/// <summary>
/// Normalizes persisted hotkey text without depending on WPF input types.
/// </summary>
public static class HotkeyTextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return value.Trim();
        }

        for (var index = 0; index < parts.Length - 1; index++)
        {
            parts[index] = NormalizeModifier(parts[index]);
        }

        parts[^1] = NormalizePrimaryKey(parts[^1]);
        return string.Join("+", parts);
    }

    private static string NormalizeModifier(string value) => value.Trim().ToLowerInvariant() switch
    {
        "control" or "ctrl" => "Ctrl",
        "alt" => "Alt",
        "shift" => "Shift",
        "windows" or "win" => "Win",
        _ => value.Trim()
    };

    private static string NormalizePrimaryKey(string value)
    {
        var key = value.Trim();
        return key.ToLowerInvariant() switch
        {
            "oem3" or "`" or "~" => "`",
            "oemminus" or "-" or "_" => "-",
            "oemplus" or "=" or "+" => "=",
            "oemopenbrackets" or "[" or "{" => "[",
            "oemclosebrackets" or "]" or "}" => "]",
            "oempipe" or "\\" or "|" => "\\",
            "oemsemicolon" or ";" or ":" => ";",
            "oemquotes" or "'" or "\"" => "'",
            "oemcomma" or "," or "<" => ",",
            "oemperiod" or "." or ">" => ".",
            "oemquestion" or "/" or "?" => "/",
            "escape" or "esc" => "Esc",
            "space" or "空格" => "Space",
            "back" or "backspace" or "退格" => "Backspace",
            "delete" or "del" or "删除" => "Del",
            "insert" or "ins" => "Ins",
            "left" or "←" => "←",
            "right" or "→" => "→",
            "up" or "↑" => "↑",
            "down" or "↓" => "↓",
            _ when key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && key.Length == 7 && char.IsDigit(key[6]) => $"Num{key[6]}",
            _ when key.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && key.Length == 4 && char.IsDigit(key[3]) => $"Num{key[3]}",
            _ => key.Length == 1 ? key.ToUpperInvariant() : key
        };
    }
}
