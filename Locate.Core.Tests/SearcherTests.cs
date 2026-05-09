using NUnit.Framework;

namespace Locate.Core.Tests;

public class SearcherTests
{
    private string _root = string.Empty;
    private Searcher _searcher = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "locate-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _searcher = new Searcher();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Touch(string relative, string content = "")
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Test]
    public void ContentMatch_OnlyContentMatches_AreReported()
    {
        Touch("a.txt", "hello fox\n");
        Touch("b.txt", "no match here\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "fox", CaseSensitive = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(Path.GetFileName(results[0].Path), Is.EqualTo("a.txt"));
        Assert.That(results[0].ContentMatches, Has.Count.EqualTo(1));
        Assert.That(results[0].NameMatches, Is.Empty);
    }

    [Test]
    public void NameSearchOff_NameOnlyMatches_AreNotReported()
    {
        Touch("fox.txt", "no match here\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "fox", CaseSensitive = true })).ToList();

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void NameSearchOn_NameOnlyMatch_IsReportedWithoutContent()
    {
        Touch("fox.txt", "nothing relevant here\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "fox", CaseSensitive = true, SearchInNames = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].ContentMatches, Is.Empty);
        Assert.That(results[0].NameMatches, Has.Count.EqualTo(1));
        Assert.That(results[0].Encoding, Is.Null);
    }

    [Test]
    public void NameSearchOn_BothMatch_BothReportedOnSameFileMatch()
    {
        Touch("fox.txt", "the fox jumped\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "fox", CaseSensitive = true, SearchInNames = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].ContentMatches, Is.Not.Empty);
        Assert.That(results[0].NameMatches, Is.Not.Empty);
        Assert.That(results[0].Encoding, Is.Not.Null);
    }

    [Test]
    public void NameSearchOn_MatchesSubdirectoryComponent()
    {
        Touch("foxhole/inner.txt", "nothing here\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "fox", CaseSensitive = true, SearchInNames = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].NameMatches, Has.Count.EqualTo(1));
    }

    [Test]
    public void EnumerationFilter_AppliesBeforeSearch()
    {
        Touch("a.cs", "fox here\n");
        Touch("a.bak", "fox there\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions { FileNamePatterns = "*.cs" },
            Search: new SearchOptions { Pattern = "fox", CaseSensitive = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(Path.GetFileName(results[0].Path), Is.EqualTo("a.cs"));
    }

    [Test]
    public void EmptyPattern_NoResults()
    {
        Touch("a.txt", "anything\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "" })).ToList();

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void Invert_YieldsFilesWithoutMatches()
    {
        var matching = Touch("matches.txt", "needle in haystack\n");
        var nonMatching1 = Touch("plain.txt", "nothing relevant\n");
        var nonMatching2 = Touch("sub/other.txt", "still nothing\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "needle", Invert = true })).ToList();

        Assert.That(results.Select(r => r.Path), Is.EquivalentTo(new[] { nonMatching1, nonMatching2 }));
        // Inverse hits carry no per-line / per-name matches.
        Assert.That(results, Has.All.With.Property(nameof(FileMatch.ContentMatches)).Empty);
        Assert.That(results, Has.All.With.Property(nameof(FileMatch.NameMatches)).Empty);
        // The matching file is excluded from inverse results.
        Assert.That(results.Select(r => r.Path), Does.Not.Contain(matching));
    }

    [Test]
    public void Invert_SkipsBinariesWhenSkipBinaryFilesIsOn()
    {
        Touch("text.txt", "harmless content\n");
        // NUL byte in the first kibibyte triggers the binary heuristic.
        var binPath = Path.Combine(_root, "blob.bin");
        File.WriteAllBytes(binPath, [0x00, 0x01, 0x02, 0x03]);

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "needle", Invert = true, SkipBinaryFiles = true })).ToList();

        Assert.That(results.Select(r => Path.GetFileName(r.Path)), Is.EquivalentTo(new[] { "text.txt" }));
    }

    [Test]
    public void SearchFiles_RestrictsToGivenPaths_IgnoringEnumerationFilters()
    {
        var a = Touch("a.txt", "needle here\n");
        var b = Touch("b.txt", "needle there too\n");
        Touch("c.txt", "needle but not in input list\n");

        var results = _searcher.SearchFiles(
            paths: new[] { a, b },
            options: new SearchOptions { Pattern = "needle" }).ToList();

        Assert.That(results.Select(r => r.Path), Is.EquivalentTo(new[] { a, b }));
        Assert.That(results, Has.All.With.Property(nameof(FileMatch.ContentMatches)).Not.Empty);
    }

    [Test]
    public void DotMatchesNewline_Off_DotDoesNotCrossNewline()
    {
        Touch("a.txt", "begin\nmiddle\nend\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions
            {
                Pattern = "begin.+end",
                UseRegex = true,
                DotMatchesNewline = false,
            })).ToList();

        Assert.That(results, Is.Empty, "Without DotMatchesNewline, the dot must not cross line boundaries.");
    }

    [Test]
    public void DotMatchesNewline_On_DotCrossesNewline_AndAnchorsToMatchStartLine()
    {
        Touch("a.txt", "begin\nmiddle\nend\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions
            {
                Pattern = "begin.+end",
                UseRegex = true,
                DotMatchesNewline = true,
            })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].ContentMatches, Has.Count.EqualTo(1));
        var hit = results[0].ContentMatches[0];
        Assert.That(hit.LineNumber, Is.EqualTo(1), "Match should anchor to the starting line.");
        Assert.That(hit.LineText, Is.EqualTo("begin"));
    }

    [Test]
    public void SearchFiles_Invert_YieldsOnlyNonMatchingPathsFromInput()
    {
        var a = Touch("a.txt", "has needle\n");
        var b = Touch("b.txt", "no n33dle\n");

        var results = _searcher.SearchFiles(
            paths: new[] { a, b },
            options: new SearchOptions { Pattern = "needle", Invert = true }).ToList();

        Assert.That(results.Select(r => r.Path), Is.EquivalentTo(new[] { b }));
    }
}
