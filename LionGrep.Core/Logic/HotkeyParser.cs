namespace LionGrep.Core.Logic;

/// <summary>
/// Parses user-supplied hotkey strings (e.g. "Ctrl+Shift+F2") into virtual-key + modifier values.
/// Numeric values intentionally match the Windows VK_* / VirtualKeyModifiers enums so the WinUI
/// code-behind can cast directly without a translation table.
/// </summary>
public static class HotkeyParser
{
    [Flags]
    public enum Modifier
    {
        None    = 0x0,
        Control = 0x1,
        Alt     = 0x2,    // matches VirtualKeyModifiers.Menu
        Shift   = 0x4,
        Windows = 0x8,
    }

    /// <param name="VirtualKeyCode">A Win32 VK_* value. <c>0</c> means "none".</param>
    public readonly record struct ParsedHotkey(int VirtualKeyCode, Modifier Modifiers);

    // VK_* values (https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes).
    private const int VkReturn  = 0x0D;
    private const int VkNumber0 = 0x30;
    private const int VkA       = 0x41;
    private const int VkF1      = 0x70;

    /// <summary>Returns true if <paramref name="hotkey"/> resolves to a combination LionGrep already
    /// uses for a built-in action (currently Ctrl+Enter for Search and Ctrl+Alt+Enter for Replace).
    /// Preset hotkey assignment must reject these so the built-ins keep working.</summary>
    public static bool IsReserved(string? hotkey)
        => TryParse(hotkey, out var parsed) && IsReserved(parsed);

    public static bool IsReserved(ParsedHotkey parsed) =>
        parsed.VirtualKeyCode == VkReturn && (
            parsed.Modifiers == Modifier.Control ||
            parsed.Modifiers == (Modifier.Control | Modifier.Alt));

    public static bool TryParse(string? hotkey, out ParsedHotkey result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var modifiers = Modifier.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control"   => Modifier.Control,
                "alt"                 => Modifier.Alt,
                "shift"               => Modifier.Shift,
                "win" or "windows"    => Modifier.Windows,
                _                     => Modifier.None,
            };
            if (mod == Modifier.None) return false;     // unrecognized modifier — bail rather than silently drop
            modifiers |= mod;
        }

        var key = ParseKeyToken(parts[^1]);
        if (key == 0) return false;

        result = new ParsedHotkey(key, modifiers);
        return true;
    }

    private static int ParseKeyToken(string token)
    {
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= '0' and <= '9') return VkNumber0 + (c - '0');
            if (c is >= 'A' and <= 'Z') return VkA + (c - 'A');
        }
        if (token.Length is >= 2 and <= 3
            && (token[0] == 'F' || token[0] == 'f')
            && int.TryParse(token.AsSpan(1), out var fn)
            && fn is >= 1 and <= 24)
        {
            return VkF1 + (fn - 1);
        }
        var lower = token.ToLowerInvariant();
        if (lower is "enter" or "return") return VkReturn;
        return 0;
    }
}
