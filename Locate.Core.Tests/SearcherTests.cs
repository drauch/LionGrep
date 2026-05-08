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
}
