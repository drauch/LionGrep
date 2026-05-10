using System.Text;
using System.Text.RegularExpressions;

namespace LionGrep.Core;

internal sealed class RegexLineReplacer : ILineReplacer
{
    private readonly Regex _regex;
    private readonly string _replacement;
    private readonly bool _preserveCase;

    public RegexLineReplacer(string pattern, bool caseSensitive, bool wholeWord, bool dotMatchesNewline, string replacement, bool preserveCase)
    {
        _regex = RegexBuilder.Build(pattern, caseSensitive, wholeWord, dotMatchesNewline);
        _replacement = replacement;
        _preserveCase = preserveCase;
    }

    public int ReplaceLine(ReadOnlySpan<char> line, StringBuilder output)
    {
        var lineStr = line.ToString();
        var count = 0;
        var result = _regex.Replace(lineStr, match =>
        {
            count++;
            var expanded = match.Result(_replacement);
            return _preserveCase ? CasePreserver.Apply(expanded, match.Value) : expanded;
        });
        output.Append(result);
        return count;
    }
}
