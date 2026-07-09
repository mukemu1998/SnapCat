using System.Windows.Input;

namespace SnapCat.App.Services;

internal readonly record struct ParsedHotkey(uint Modifiers, uint Key);

internal static class HotkeyParser
{
    public static bool TryParse(string text, out ParsedHotkey hotkey)
    {
        hotkey = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = 0u;
        var key = 0u;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            var isLast = index == parts.Length - 1;

            if (!isLast)
            {
                var modifier = part.ToLowerInvariant() switch
                {
                    "ctrl" or "control" => 0x0002u,
                    "alt" => 0x0001u,
                    "shift" => 0x0004u,
                    "win" or "windows" => 0x0008u,
                    _ => uint.MaxValue
                };

                if (modifier == uint.MaxValue)
                {
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (HotkeyTextFormatter.TryGetPrimaryKeyFromText(part, out var wpfKey))
            {
                key = (uint)KeyInterop.VirtualKeyFromKey(wpfKey);
                if (key == 0)
                {
                    return false;
                }

                hotkey = new ParsedHotkey(modifiers, key);
                return true;
            }

            return false;
        }

        return false;
    }
}
