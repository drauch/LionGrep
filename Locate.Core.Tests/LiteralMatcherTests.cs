using NUnit.Framework;

namespace Locate.Core.Tests;

public class LiteralMatcherTests
{
    private static List<MatchSpan> Find(SearchOptions options, string line)
    {
        var matcher = MatcherFactory.Create(options);
        var result = new List<MatchSpan>();
        matcher.FindMatches(line, result);
        return result;
    }

    [Test]
    public void CaseSensitive_OnlyMatchesExactCase()
    {
        var hits = Find(new SearchOptions { Pattern = "Foo", CaseSensitive = true }, "Foo foo FOO");
        Assert.That(hits, Is.EqualTo(new[] { new MatchSpan(0, 3) }));
    }

    [Test]
    public void CaseInsensitive_MatchesAllCasings()
    {
        var hits = Find(new SearchOptions { Pattern = "foo" }, "Foo foo FOO");
        Assert.That(hits, Is.EqualTo(new[]
        {
            new MatchSpan(0, 3),
            new MatchSpan(4, 3),
            new MatchSpan(8, 3),
        }));
    }

    [Test]
    public void OverlappingPattern_AdvancesByOne()
    {
        // "aa" inside "aaaa" yields three overlapping matches at columns 0,1,2.
        var hits = Find(new SearchOptions { Pattern = "aa", CaseSensitive = true }, "aaaa");
        Assert.That(hits.Count, Is.EqualTo(3));
        Assert.That(hits[0].Column, Is.EqualTo(0));
        Assert.That(hits[1].Column, Is.EqualTo(1));
        Assert.That(hits[2].Column, Is.EqualTo(2));
    }

    [Test]
    public void WholeWord_RejectsMidWordMatches()
    {
        var hits = Find(new SearchOptions { Pattern = "cat", WholeWord = true, CaseSensitive = true },
            "cat catalog scattered the cat");
        // Matches at start ("cat ") and end (" cat"); rejects "cat" inside "catalog" and "scattered".
        Assert.That(hits.Select(h => h.Column), Is.EqualTo(new[] { 0, 26 }));
    }

    [Test]
    public void WholeWord_AcceptsBoundariesOfPunctuation()
    {
        var hits = Find(new SearchOptions { Pattern = "id", WholeWord = true, CaseSensitive = true },
            "id=1, id_x, (id)");
        Assert.That(hits.Select(h => h.Column), Is.EqualTo(new[] { 0, 13 }));
    }

    [Test]
    public void EmptyPattern_YieldsNoMatches()
    {
        var hits = Find(new SearchOptions { Pattern = "" }, "anything");
        Assert.That(hits, Is.Empty);
    }

    [Test]
    public void OrdinalSemantics_DoesNotFoldEszett()
    {
        // German ß ↔ ss should NOT match under ordinal rules — that's the whole point of avoiding culture-aware compare.
        var hits = Find(new SearchOptions { Pattern = "ss" }, "Straße");
        Assert.That(hits, Is.Empty);
    }

    [Test]
    public void CaseInsensitive_FoldsNonAsciiViaToUpperInvariant()
    {
        // OrdinalIgnoreCase isn't ASCII-only — it folds char-by-char via ToUpperInvariant,
        // which covers single-char Unicode case pairs like Cyrillic К/к.
        // It still does NOT do multi-char foldings (see eszett test above).
        var hits = Find(new SearchOptions { Pattern = "к" }, "К");
        Assert.That(hits, Is.EqualTo(new[] { new MatchSpan(0, 1) }));
    }
}
