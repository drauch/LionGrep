using System.Text;

namespace Locate.Core;

internal sealed class LiteralMatcher : IMatcher
{
    private readonly string _pattern;
    private readonly StringComparison _comparison;
    private readonly bool _wholeWord;
    private readonly byte[] _utf8PatternBytes;
    private readonly byte[]? _asciiLowerPatternBytes;
    private readonly bool _isAscii;

    public LiteralMatcher(string pattern, bool caseSensitive, bool wholeWord)
    {
        _pattern = pattern;
        _comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        _wholeWord = wholeWord;
        _utf8PatternBytes = pattern.Length == 0 ? [] : Encoding.UTF8.GetBytes(pattern);
        _isAscii = IsAllAscii(pattern);
        _asciiLowerPatternBytes = (!caseSensitive && _isAscii && pattern.Length > 0)
            ? BuildAsciiLowerBytes(pattern)
            : null;
    }

    /// <summary>UTF-8 encoding of the pattern. Always populated. Used directly by the case-sensitive byte-level
    /// fast path — UTF-8 is self-synchronizing, so any multi-byte character's bytes only appear at that character's
    /// position in valid UTF-8 content, making byte-level <c>IndexOf</c> correct for any pattern.</summary>
    internal byte[] Utf8PatternBytes => _utf8PatternBytes;

    /// <summary>Char count of the pattern (NOT the byte count). The byte-fast-path needs this to
    /// report <see cref="LineMatch.Length"/> in chars — consumers index into the decoded line text,
    /// which is char-based, so handing them a UTF-8 byte count would overshoot the highlight on
    /// non-ASCII patterns (e.g. <c>"Größe"</c> = 5 chars / 7 bytes).</summary>
    internal int PatternCharCount => _pattern.Length;

    /// <summary>Pre-folded lowercase ASCII bytes — only populated when the pattern is pure ASCII and the matcher
    /// is case-insensitive. Lets the fast path do <c>IndexOfAny(lower, upper)</c> skip-scanning followed by
    /// case-insensitive byte comparison without going through char decoding.</summary>
    internal byte[]? AsciiLowerPatternBytes => _asciiLowerPatternBytes;

    /// <summary>True iff every char in the pattern is &lt; 128.</summary>
    internal bool IsAsciiPattern => _isAscii;

    internal bool CaseSensitive => _comparison == StringComparison.Ordinal;
    internal bool WholeWord => _wholeWord;

    private static bool IsAllAscii(string s) => Ascii.IsValid(s.AsSpan());

    private static byte[] BuildAsciiLowerBytes(string pattern)
    {
        var bytes = new byte[pattern.Length];
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            bytes[i] = (byte)(c is >= 'A' and <= 'Z' ? c + 32 : c);
        }
        return bytes;
    }

    public void FindMatches(ReadOnlySpan<char> line, ICollection<MatchSpan> destination)
    {
        if (_pattern.Length == 0)
            return;

        var pattern = _pattern.AsSpan();
        var index = 0;
        while (index <= line.Length - pattern.Length)
        {
            var found = line[index..].IndexOf(pattern, _comparison);
            if (found < 0)
                return;

            var column = index + found;
            if (!_wholeWord || IsWholeWord(line, column, pattern.Length))
                destination.Add(new MatchSpan(column, pattern.Length));

            index = column + 1;
        }
    }

    private static bool IsWholeWord(ReadOnlySpan<char> line, int column, int length)
    {
        if (column > 0 && IsWordChar(line[column - 1]))
            return false;
        var end = column + length;
        if (end < line.Length && IsWordChar(line[end]))
            return false;
        return true;
    }

    private static bool IsWordChar(char c) =>
        (uint)(c - '0') <= 9 ||
        (uint)((c | 0x20) - 'a') <= 25 ||
        c == '_';
}
