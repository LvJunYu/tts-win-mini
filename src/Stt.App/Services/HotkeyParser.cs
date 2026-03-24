using System.Windows.Forms;
using System.Windows.Input;

namespace Stt.App.Services;

public static class HotkeyParser
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public static bool TryParse(string? text, out HotkeyGesture? gesture, out string? error)
    {
        gesture = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey cannot be empty.";
            return false;
        }

        var tokens = text
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        uint modifiers = 0;
        Keys? key = null;

        foreach (var token in tokens)
        {
            switch (token.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModControl;
                    continue;
                case "ALT":
                    modifiers |= ModAlt;
                    continue;
                case "SHIFT":
                    modifiers |= ModShift;
                    continue;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModWin;
                    continue;
            }

            if (key is not null)
            {
                error = $"Only one non-modifier key is allowed in a hotkey: {text}.";
                return false;
            }

            if (!TryParseKey(token, out var parsedKey))
            {
                error = $"Unsupported hotkey key: {token}.";
                return false;
            }

            key = parsedKey;
        }

        if (key is null)
        {
            error = "Hotkey needs one key, for example F8 or Ctrl+Alt+Space.";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key.Value, FormatDisplayText(modifiers, key.Value));
        return true;
    }

    public static uint ToModifierFlags(ModifierKeys modifiers)
    {
        uint flags = 0;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            flags |= ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            flags |= ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            flags |= ModShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            flags |= ModWin;
        }

        return flags;
    }

    public static string FormatDisplayText(uint modifiers, Keys key)
    {
        var normalizedTokens = new List<string>();

        if ((modifiers & ModControl) != 0)
        {
            normalizedTokens.Add("Ctrl");
        }

        if ((modifiers & ModAlt) != 0)
        {
            normalizedTokens.Add("Alt");
        }

        if ((modifiers & ModShift) != 0)
        {
            normalizedTokens.Add("Shift");
        }

        if ((modifiers & ModWin) != 0)
        {
            normalizedTokens.Add("Win");
        }

        normalizedTokens.Add(FormatKeyDisplayText(key));

        return string.Join("+", normalizedTokens.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryParseKey(string token, out Keys key)
    {
        key = Keys.None;

        if (token.Length == 1)
        {
            var character = token[0];
            if (char.IsLetter(character))
            {
                key = Enum.Parse<Keys>(char.ToUpperInvariant(character).ToString());
                return true;
            }

            if (char.IsDigit(character))
            {
                key = Enum.Parse<Keys>($"D{character}");
                return true;
            }
        }

        return token.ToUpperInvariant() switch
        {
            "SPACE" => SetKey(Keys.Space, out key),
            "TAB" => SetKey(Keys.Tab, out key),
            "ENTER" => SetKey(Keys.Enter, out key),
            "ESC" => SetKey(Keys.Escape, out key),
            "UP" => SetKey(Keys.Up, out key),
            "DOWN" => SetKey(Keys.Down, out key),
            "LEFT" => SetKey(Keys.Left, out key),
            "RIGHT" => SetKey(Keys.Right, out key),
            "HOME" => SetKey(Keys.Home, out key),
            "END" => SetKey(Keys.End, out key),
            "PGUP" or "PAGEUP" => SetKey(Keys.PageUp, out key),
            "PGDN" or "PAGEDOWN" => SetKey(Keys.PageDown, out key),
            "BACKSPACE" => SetKey(Keys.Back, out key),
            "DELETE" => SetKey(Keys.Delete, out key),
            "INSERT" => SetKey(Keys.Insert, out key),
            _ => TryParseFunctionKey(token, out key)
        };
    }

    private static bool TryParseFunctionKey(string token, out Keys key)
    {
        key = Keys.None;

        if (!token.StartsWith("F", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(token[1..], out var number) || number < 1 || number > 24)
        {
            return false;
        }

        key = (Keys)((int)Keys.F1 + (number - 1));
        return true;
    }

    private static bool SetKey(Keys value, out Keys key)
    {
        key = value;
        return true;
    }

    private static string FormatKeyDisplayText(Keys key)
    {
        if (key is >= Keys.D0 and <= Keys.D9)
        {
            return ((int)(key - Keys.D0)).ToString();
        }

        return key switch
        {
            Keys.Space => "Space",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.Escape => "Esc",
            Keys.Back => "Backspace",
            Keys.Delete => "Delete",
            _ => key.ToString()
        };
    }

}
