using LionGrep.Core.Logic;
using NUnit.Framework;

namespace LionGrep.Core.Tests.Logic;

public class HotkeyParserTests
{
    [TestCase("F2",          0x71, HotkeyParser.Modifier.None)]
    [TestCase("f2",          0x71, HotkeyParser.Modifier.None)]   // R14 — lowercase F
    [TestCase("ctrl+f12",    0x7B, HotkeyParser.Modifier.Control)]
    [TestCase("Ctrl+1",      0x31, HotkeyParser.Modifier.Control)]
    [TestCase("Ctrl+Shift+F",0x46, HotkeyParser.Modifier.Control | HotkeyParser.Modifier.Shift)]
    [TestCase("Alt+F2",      0x71, HotkeyParser.Modifier.Alt)]
    [TestCase("Win+A",       0x41, HotkeyParser.Modifier.Windows)]
    [TestCase("control+z",   0x5A, HotkeyParser.Modifier.Control)]   // case-insensitive modifier name
    [TestCase("Ctrl+Alt+Shift+F12", 0x7B, HotkeyParser.Modifier.Control | HotkeyParser.Modifier.Alt | HotkeyParser.Modifier.Shift)]
    [TestCase("F24",         0x87, HotkeyParser.Modifier.None)]
    public void Parses_StandardHotkeys(string input, int expectedVk, HotkeyParser.Modifier expectedMods)
    {
        Assert.That(HotkeyParser.TryParse(input, out var parsed), Is.True);
        Assert.That(parsed.VirtualKeyCode, Is.EqualTo(expectedVk));
        Assert.That(parsed.Modifiers, Is.EqualTo(expectedMods));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("   ")]
    [TestCase("F25")]                        // out of range
    [TestCase("F0")]                         // out of range
    [TestCase("Ctrl+Foo")]                   // unrecognized key token
    [TestCase("Bogus+A")]                    // unrecognized modifier
    [TestCase("Ctrl++")]                     // empty key after the trailing +
    [TestCase("AB")]                         // multi-letter key
    public void Rejects_InvalidInput(string? input)
    {
        Assert.That(HotkeyParser.TryParse(input, out _), Is.False);
    }

    [TestCase("Enter",          0x0D)]
    [TestCase("enter",          0x0D)]
    [TestCase("Return",         0x0D)]
    [TestCase("ctrl+enter",     0x0D)]
    public void Parses_EnterAndReturn(string input, int expectedVk)
    {
        Assert.That(HotkeyParser.TryParse(input, out var parsed), Is.True);
        Assert.That(parsed.VirtualKeyCode, Is.EqualTo(expectedVk));
    }

    // ---- IsReserved: Ctrl+Enter (Search) and Ctrl+Alt+Enter (Replace) are off-limits to presets ----

    [TestCase("Ctrl+Enter")]
    [TestCase("ctrl+enter")]
    [TestCase("control+return")]
    [TestCase("Ctrl+Alt+Enter")]
    [TestCase("Alt+Ctrl+Enter")]    // modifier order shouldn't matter — both must reject
    [TestCase("Ctrl+Alt+Return")]
    public void IsReserved_BlocksBuiltInBindings(string input)
    {
        Assert.That(HotkeyParser.IsReserved(input), Is.True);
    }

    [TestCase("Ctrl+1")]
    [TestCase("F2")]
    [TestCase("Alt+Enter")]            // not reserved (Replace requires Ctrl)
    [TestCase("Shift+Enter")]
    [TestCase("Ctrl+Shift+Enter")]     // not reserved (Replace is exactly Ctrl+Alt+Enter)
    [TestCase("Ctrl+Shift+F")]
    [TestCase("")]                     // empty input — not reserved (and not parseable either)
    [TestCase(null)]
    public void IsReserved_LetsEverythingElseThrough(string? input)
    {
        Assert.That(HotkeyParser.IsReserved(input), Is.False);
    }
}
