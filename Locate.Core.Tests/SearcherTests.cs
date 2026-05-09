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
    public void AsciiFastPath_CaseInsensitive_FindsMixedCaseHits()
    {
        Touch("a.txt", "Hello WORLD\nhello world\nHELLO\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "hello", CaseSensitive = false })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        // 3 lines, each contains a hit, regardless of casing.
        Assert.That(results[0].ContentMatches, Has.Count.EqualTo(3));
    }

    [Test]
    public void AsciiFastPath_WholeWord_BoundariesRespected()
    {
        Touch("a.txt", "foo\nfoobar\nbar foo\nbarfoo\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "foo", WholeWord = true, CaseSensitive = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        // "foo" alone (line 1) and "bar foo" (line 3) match; "foobar" and "barfoo" are not whole words.
        Assert.That(results[0].ContentMatches.Select(m => m.LineNumber), Is.EquivalentTo(new[] { 1, 3 }));
    }

    [Test]
    public void AsciiFastPath_NonAsciiBeforeMatch_ColumnReportedInChars()
    {
        // German umlauts before the ASCII match — char column != byte column.
        Touch("a.txt", "üöä foo bar\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "foo", CaseSensitive = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        var hit = results[0].ContentMatches[0];
        // "üöä " is 4 chars but 7 UTF-8 bytes. Column must be the char index, not the byte offset.
        Assert.That(hit.Column, Is.EqualTo(4));
    }

    [Test]
    public void NonAsciiLiteral_CaseSensitive_GoesThroughByteFastPath()
    {
        // German umlauts in pattern; UTF-8 self-synchronization means byte-level IndexOf is safe.
        Touch("a.txt", "Größe: 42\nGroessenangabe\nNur Größe ist Größe.\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "Größe", CaseSensitive = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        // line 1: "Größe: 42"  → 1 hit
        // line 3: "Nur Größe ist Größe." → 2 hits
        Assert.That(results[0].ContentMatches.Select(m => m.LineNumber).OrderBy(x => x), Is.EqualTo(new[] { 1, 3, 3 }));
    }

    [Test]
    public void NonAsciiLiteral_HighlightLength_IsCharCount_NotByteCount()
    {
        // R5 — "Größe" is 5 chars but 7 UTF-8 bytes. The byte-fast-path used to report 7 as
        // LineMatch.Length, which the UI treats as a char count and overshoots the highlight by
        // 2 chars. After the fix, Length should be the char count (5).
        Touch("a.txt", "var name = \"Größe\";\n");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "Größe", CaseSensitive = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        var hit = results[0].ContentMatches.Single();
        Assert.That(hit.Length, Is.EqualTo(5),
            "Length must be the pattern's CHAR count, not its UTF-8 byte count.");
        Assert.That(hit.LineText.Substring(hit.Column, hit.Length), Is.EqualTo("Größe"),
            "Highlight extracted via Substring(Column, Length) should equal the matched pattern exactly.");
    }

    [Test]
    public void RegexPrefilter_StillReturnsAllMatches_AndIgnoresFilesWithoutLiteral()
    {
        Touch("hits.txt", "class Foo {}\n");           // contains "class" — passes prefilter, regex matches.
        Touch("noclass.txt", "interface IFoo {}\n");   // no "class" — prefilter rejects without running regex.
        Touch("noise.txt", "klass Foo\n");              // close but no cigar — prefilter rejects.

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions
            {
                Pattern = @"class\s+\w+",
                UseRegex = true,
                CaseSensitive = true,
            })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(Path.GetFileName(results[0].Path), Is.EqualTo("hits.txt"));
        Assert.That(results[0].ContentMatches, Has.Count.EqualTo(1));
    }

    [Test]
    public void MmapPath_LargeFile_FindsMatchInsideTheMmappedRange()
    {
        // R13 — every existing test creates files of a few hundred bytes, which fall under the
        // 64 KB MmapMinBytes threshold and go through File.ReadAllBytes. Force the mmap path with
        // a > 64 KB file so the unsafe pointer / SafeMemoryMappedViewHandle code is exercised.
        const int FillerLines = 4_000;
        var sb = new System.Text.StringBuilder(capacity: FillerLines * 32);
        for (var i = 0; i < FillerLines; i++)
            sb.AppendLine($"filler line {i:D5} with no token here");
        // Splice the needle on a known line so we can assert its position.
        sb.AppendLine("the magic NEEDLE_TOKEN appears exactly once on this line");

        Touch("big.txt", sb.ToString());
        var fileLength = new FileInfo(Path.Combine(_root, "big.txt")).Length;
        Assert.That(fileLength, Is.GreaterThan(64 * 1024),
            "Test setup must produce a file larger than the mmap threshold to exercise the right path.");

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "NEEDLE_TOKEN", CaseSensitive = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].ContentMatches, Has.Count.EqualTo(1));
        Assert.That(results[0].ContentMatches[0].LineNumber, Is.EqualTo(FillerLines + 1));
    }

    [Test]
    public void MmapPath_LargeFileWithDenseMatches_AllReportedCorrectly()
    {
        // R13 part 2 — exercise the cached-line-text fast path and high match counts in the mmap branch.
        const int Repetitions = 500;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(new string('x', 60_000));   // pad past the 64 KB threshold first
        for (var i = 0; i < Repetitions; i++)
            sb.AppendLine("hit hit hit");          // 3 hits per line, 500 lines
        Touch("dense.txt", sb.ToString());

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "hit", CaseSensitive = true })).ToList();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].ContentMatches, Has.Count.EqualTo(Repetitions * 3));
    }

    [Test]
    public void Producer_NonOceException_PropagatesToCaller_NotSilentlySwallowed()
    {
        // R8 — workers throwing anything other than OperationCanceledException used to fault the
        // producer Task, then DrainQueue's `try { producer.Wait(); } catch { /* observed */ }` would
        // silently swallow it, leaving the user with "0 matches" and zero indication of failure.
        // The fix re-raises non-OCE exceptions through the consumer-side iterator.
        Touch("a.txt", "anything\n");
        Touch("b.txt", "anything\n");

        var throwingSearcher = new Searcher(_ => new ThrowingMatcher());
        Assert.Throws<AggregateException>(() =>
        {
            foreach (var _ in throwingSearcher.Search(new SearchRequest(
                Roots: [_root],
                Enumeration: new FileEnumerationOptions(),
                Search: new SearchOptions { Pattern = "anything" }))) { }
        });
    }

    private sealed class ThrowingMatcher : IMatcher
    {
        public void FindMatches(ReadOnlySpan<char> line, ICollection<MatchSpan> destination)
            => throw new InvalidOperationException("intentional test failure");
    }

    [Test]
    public void Parallel_AllMatchesYielded_NoDuplicates_NoDrops()
    {
        // Force enough files that the parallel scheduler will dispatch across multiple workers.
        const int FileCount = 200;
        var expected = new HashSet<string>();
        for (var i = 0; i < FileCount; i++)
        {
            var path = Touch($"f{i:D3}.txt", $"line {i}\nneedle here {i}\nlast line {i}\n");
            expected.Add(path);
        }

        var results = _searcher.Search(new SearchRequest(
            Roots: [_root],
            Enumeration: new FileEnumerationOptions(),
            Search: new SearchOptions { Pattern = "needle" })).ToList();

        // Every file should be matched exactly once.
        var paths = results.Select(r => r.Path).ToList();
        Assert.That(paths, Has.Count.EqualTo(FileCount));
        Assert.That(paths.Distinct().Count(), Is.EqualTo(FileCount), "parallel pipeline must not duplicate.");
        Assert.That(new HashSet<string>(paths), Is.EquivalentTo(expected), "parallel pipeline must not drop.");
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
