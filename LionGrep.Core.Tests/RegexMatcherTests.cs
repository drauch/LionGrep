using NUnit.Framework;

namespace LionGrep.Core.Tests;

public class RegexMatcherTests
{
    private static List<MatchSpan> Find(SearchOptions options, string line)
    {
        var matcher = MatcherFactory.Create(options);
        var result = new List<MatchSpan>();
        matcher.FindMatches(line, result);
        return result;
    }

    [Test]
    public void BasicRegex_FindsAllMatches()
    {
        var hits = Find(new SearchOptions { Pattern = @"\d+", UseRegex = true, CaseSensitive = true },
            "abc 12 def 345 ghi");
        Assert.That(hits, Is.EqualTo(new[] { new MatchSpan(4, 2), new MatchSpan(11, 3) }));
    }

    [Test]
    public void IgnoreCase_AppliesToRegex()
    {
        var hits = Find(new SearchOptions { Pattern = "hello", UseRegex = true },
            "Hello HELLO hello");
        Assert.That(hits.Count, Is.EqualTo(3));
    }

    [Test]
    public void WholeWord_WrapsPatternInBoundaries()
    {
        var hits = Find(new SearchOptions { Pattern = "cat", UseRegex = true, WholeWord = true, CaseSensitive = true },
            "cat catalog the cat");
        Assert.That(hits.Select(h => h.Column), Is.EqualTo(new[] { 0, 16 }));
    }

    [Test]
    public void ZeroWidthMatches_AreSkipped()
    {
        var hits = Find(new SearchOptions { Pattern = "a*", UseRegex = true, CaseSensitive = true },
            "bbb");
        Assert.That(hits, Is.Empty);
    }

    [Test]
    public void DotMatchesNewline_ToggleAffectsSingleline()
    {
        // Within a single line we can't really observe Singleline (no \n inside one line).
        // But we can verify the option compiles and dot still matches anything else.
        var hits = Find(new SearchOptions { Pattern = "a.c", UseRegex = true, DotMatchesNewline = true, CaseSensitive = true },
            "abc axc");
        Assert.That(hits.Count, Is.EqualTo(2));
    }
}
