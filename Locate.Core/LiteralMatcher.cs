namespace Locate.Core;

internal sealed class LiteralMatcher : IMatcher
{
    private readonly string _pattern;
    private readonly StringComparison _comparison;
    private readonly bool _wholeWord;
    private readonly byte[]? _asciiPatternBytes;

    public LiteralMatcher(string pattern, bool caseSensitive, bool wholeWord)
    {
        _pattern = pattern;
        _comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        _wholeWord = wholeWord;
        _asciiPatternBytes = TryBuildAsciiBytes(pattern, caseSensitive);
    }

    /// <summary>Pattern as bytes for byte-level scan over UTF-8 content, or null if the pattern isn't pure ASCII.
    /// When the matcher is case-insensitive, bytes are pre-folded to lowercase so the fast path can compare against
    /// each candidate byte's lowercase form.</summary>
    internal byte[]? AsciiPatternBytes => _asciiPatternBytes;
    internal bool CaseSensitive => _comparison == StringComparison.Ordinal;
    internal bool WholeWord => _wholeWord;

    private static byte[]? TryBuildAsciiBytes(string pattern, bool caseSensitive)
    {
        if (pattern.Length == 0) return null;
        var bytes = new byte[pattern.Length];
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c >= 128) return null;
            bytes[i] = caseSensitive
                ? (byte)c
                : (byte)(c is >= 'A' and <= 'Z' ? c + 32 : c);
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
