using Locate.Core.Logic;
using NUnit.Framework;

namespace Locate.Core.Tests.Logic;

public class HighlightSegmenterTests
{
    private static HighlightRange[] Eng(params (int Start, int Length)[] ranges) =>
        ranges.Select(r => new HighlightRange(r.Start, r.Length)).ToArray();

    [Test]
    public void EmptyText_ReturnsEmpty()
    {
        var result = HighlightSegmenter.Build("", null, null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void NoMatchesAndNoFilter_ReturnsSingleNoneSegment()
    {
        var result = HighlightSegmenter.Build("hello world", null, null);
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("hello world", HighlightKind.None),
        }));
    }

    [Test]
    public void EngineMatchOnly_SplitsAroundMatch()
    {
        var result = HighlightSegmenter.Build("the quick fox", Eng((10, 3)), filterText: null);
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("the quick ", HighlightKind.None),
            new HighlightSegment("fox", HighlightKind.EngineMatch),
        }));
    }

    [Test]
    public void FilterOnly_HighlightsEveryOccurrence_CaseInsensitive()
    {
        var result = HighlightSegmenter.Build("Foo bar foo BAZ FOO", null, "foo");
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("Foo", HighlightKind.FilterMatch),
            new HighlightSegment(" bar ", HighlightKind.None),
            new HighlightSegment("foo", HighlightKind.FilterMatch),
            new HighlightSegment(" BAZ ", HighlightKind.None),
            new HighlightSegment("FOO", HighlightKind.FilterMatch),
        }));
    }

    [Test]
    public void EngineAndFilter_NonOverlapping_BothHighlighted()
    {
        // "fox" is the engine match (yellow); "lazy" is the filter (blue) — no overlap.
        var result = HighlightSegmenter.Build("the fox over the lazy dog", Eng((4, 3)), "lazy");
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("the ", HighlightKind.None),
            new HighlightSegment("fox", HighlightKind.EngineMatch),
            new HighlightSegment(" over the ", HighlightKind.None),
            new HighlightSegment("lazy", HighlightKind.FilterMatch),
            new HighlightSegment(" dog", HighlightKind.None),
        }));
    }

    [Test]
    public void FilterOverlapsEngineMatch_FilterWins_InOverlapOnly()
    {
        // engine: "fox" at 4..7. filter "ox" overlaps the last 2 chars of the engine match.
        var result = HighlightSegmenter.Build("the fox here", Eng((4, 3)), "ox");
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("the ", HighlightKind.None),
            new HighlightSegment("f", HighlightKind.EngineMatch),
            new HighlightSegment("ox", HighlightKind.FilterMatch),
            new HighlightSegment(" here", HighlightKind.None),
        }));
    }

    [Test]
    public void FilterFullyContainsEngineMatch_AllFilter()
    {
        // engine: "ox" at 5..7. Filter "fox" subsumes the engine match entirely.
        var result = HighlightSegmenter.Build("the fox here", Eng((5, 2)), "fox");
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("the ", HighlightKind.None),
            new HighlightSegment("fox", HighlightKind.FilterMatch),
            new HighlightSegment(" here", HighlightKind.None),
        }));
    }

    [Test]
    public void EngineFullyContainsFilter_OnlyOverlapIsFilter()
    {
        // engine: "abcde" at 0..5. filter "cd" inside.
        var result = HighlightSegmenter.Build("abcdefg", Eng((0, 5)), "cd");
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("ab", HighlightKind.EngineMatch),
            new HighlightSegment("cd", HighlightKind.FilterMatch),
            new HighlightSegment("e", HighlightKind.EngineMatch),
            new HighlightSegment("fg", HighlightKind.None),
        }));
    }

    [Test]
    public void MultipleAdjacentEngineMatches_Coalesce()
    {
        // Two engine matches that touch — should produce one merged segment.
        var result = HighlightSegmenter.Build("foofoo", Eng((0, 3), (3, 3)), filterText: null);
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("foofoo", HighlightKind.EngineMatch),
        }));
    }

    [Test]
    public void OverlappingFilterOccurrences_AllHighlighted()
    {
        // filter "aa" in "aaaa" — overlapping matches. We advance by 1 between hits, so every
        // character that falls inside any occurrence is highlighted.
        var result = HighlightSegmenter.Build("aaaa", null, "aa");
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("aaaa", HighlightKind.FilterMatch),
        }));
    }

    [Test]
    public void EngineMatchAtClampedBoundary_DoesNotOverflow()
    {
        // engine range extends past end of text — should be clamped, not throw.
        var result = HighlightSegmenter.Build("hello", Eng((3, 99)), filterText: null);
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("hel", HighlightKind.None),
            new HighlightSegment("lo", HighlightKind.EngineMatch),
        }));
    }

    [Test]
    public void EngineMatchWithNegativeStart_ClampedToZero()
    {
        var result = HighlightSegmenter.Build("hello", Eng((-2, 4)), filterText: null);
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("he", HighlightKind.EngineMatch),
            new HighlightSegment("llo", HighlightKind.None),
        }));
    }

    [Test]
    public void FilterWithEmptyString_TreatedAsNoFilter()
    {
        var result = HighlightSegmenter.Build("hello", null, "");
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("hello", HighlightKind.None),
        }));
    }

    [Test]
    public void FilterCaseInsensitive_BoundaryCharactersIntact()
    {
        // Verify exact char-preservation (not lowercased) in the segment Text.
        var result = HighlightSegmenter.Build("XYZ", null, "xyz");
        Assert.That(result, Is.EqualTo(new[]
        {
            new HighlightSegment("XYZ", HighlightKind.FilterMatch),
        }));
        Assert.That(result[0].Text, Is.EqualTo("XYZ"), "segment text must preserve original casing");
    }
}
