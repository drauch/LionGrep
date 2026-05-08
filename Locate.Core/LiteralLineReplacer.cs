using System.Text;

namespace Locate.Core;

internal sealed class LiteralLineReplacer : ILineReplacer
{
    private readonly LiteralMatcher _matcher;
    private readonly string _replacement;
    private readonly bool _preserveCase;
    private readonly List<MatchSpan> _scratch = new(8);

    public LiteralLineReplacer(string pattern, bool caseSensitive, bool wholeWord, string replacement, bool preserveCase)
    {
        _matcher = new LiteralMatcher(pattern, caseSensitive, wholeWord);
        _replacement = replacement;
        _preserveCase = preserveCase;
    }

    public int ReplaceLine(ReadOnlySpan<char> line, StringBuilder output)
    {
        _scratch.Clear();
        _matcher.FindMatches(line, _scratch);
        if (_scratch.Count == 0)
        {
            output.Append(line);
            return 0;
        }

        var cursor = 0;
        foreach (var span in _scratch)
        {
            output.Append(line[cursor..span.Column]);
            var matched = line.Slice(span.Column, span.Length);
            var rep = _preserveCase ? CasePreserver.Apply(_replacement, matched) : _replacement;
            output.Append(rep);
            cursor = span.Column + span.Length;
        }
        output.Append(line[cursor..]);
        return _scratch.Count;
    }
}
