namespace Locate.Core;

internal sealed class LiteralMatcher : IMatcher
{
    private readonly string _pattern;
    private readonly StringComparison _comparison;
    private readonly bool _wholeWord;

    public LiteralMatcher(string pattern, bool caseSensitive, bool wholeWord)
    {
        _pattern = pattern;
        _comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        _wholeWord = wholeWord;
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
