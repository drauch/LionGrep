using System.Text.RegularExpressions;

namespace Locate.Core;

internal sealed class RegexMatcher : IMatcher
{
    private readonly Regex _regex;

    public RegexMatcher(string pattern, bool caseSensitive, bool wholeWord, bool dotMatchesNewline)
        => _regex = RegexBuilder.Build(pattern, caseSensitive, wholeWord, dotMatchesNewline);

    public void FindMatches(ReadOnlySpan<char> line, ICollection<MatchSpan> destination)
    {
        foreach (var match in _regex.EnumerateMatches(line))
        {
            if (match.Length == 0)
                continue;
            destination.Add(new MatchSpan(match.Index, match.Length));
        }
    }
}
