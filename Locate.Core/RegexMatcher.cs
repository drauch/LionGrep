using System.Text;
using System.Text.RegularExpressions;

namespace Locate.Core;

internal sealed class RegexMatcher : IMatcher
{
    private readonly Regex _regex;
    private readonly byte[]? _requiredLiteralUtf8;
    private readonly byte[]? _requiredLiteralAsciiLower;
    private readonly bool _requiredLiteralIsAscii;
    private readonly bool _caseSensitive;

    public RegexMatcher(string pattern, bool caseSensitive, bool wholeWord, bool dotMatchesNewline)
    {
        _regex = RegexBuilder.Build(pattern, caseSensitive, wholeWord, dotMatchesNewline);
        _caseSensitive = caseSensitive;

        var literal = RegexLiteralExtractor.TryExtractRequiredLiteral(pattern);
        if (literal is not null)
        {
            _requiredLiteralUtf8 = Encoding.UTF8.GetBytes(literal);
            _requiredLiteralIsAscii = IsAllAscii(literal);
            _requiredLiteralAsciiLower = (!caseSensitive && _requiredLiteralIsAscii)
                ? BuildAsciiLowerBytes(literal)
                : null;
        }
    }

    /// <summary>UTF-8 bytes of a substring the regex must contain to match — used as a cheap
    /// pre-filter so we can skip running the regex engine entirely on files that don't contain it.
    /// Null if no useful literal could be extracted.</summary>
    internal byte[]? RequiredLiteralUtf8 => _requiredLiteralUtf8;

    /// <summary>For case-insensitive ASCII pre-filters, the literal pre-folded to lowercase.</summary>
    internal byte[]? RequiredLiteralAsciiLower => _requiredLiteralAsciiLower;

    internal bool RequiredLiteralIsAscii => _requiredLiteralIsAscii;
    internal bool CaseSensitive => _caseSensitive;

    public void FindMatches(ReadOnlySpan<char> line, ICollection<MatchSpan> destination)
    {
        foreach (var match in _regex.EnumerateMatches(line))
        {
            if (match.Length == 0)
                continue;
            destination.Add(new MatchSpan(match.Index, match.Length));
        }
    }

    private static bool IsAllAscii(string s) => Ascii.IsValid(s.AsSpan());

    private static byte[] BuildAsciiLowerBytes(string s)
    {
        var bytes = new byte[s.Length];
        for (var i = 0; i < s.Length; i++)
            bytes[i] = (byte)(s[i] is >= 'A' and <= 'Z' ? s[i] + 32 : s[i]);
        return bytes;
    }
}
