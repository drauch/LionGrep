using System.Text;
using NUnit.Framework;

namespace Locate.Core.Tests;

public class FileSearcherTests
{
    private string _tempDir = string.Empty;
    private FileSearcher _searcher = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "locate-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _searcher = new FileSearcher();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteText(string name, string content, Encoding encoding)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray());
        return path;
    }

    [Test]
    public void EmptyFile_ReturnsNull()
    {
        var path = WriteFile("empty.txt", []);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "x" });
        Assert.That(_searcher.Search(path, matcher), Is.Null);
    }

    [Test]
    public void NoMatches_ReturnsNull()
    {
        var path = WriteFile("no.txt", "hello world\nfoo bar\n"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "missing", CaseSensitive = true });
        Assert.That(_searcher.Search(path, matcher), Is.Null);
    }

    [Test]
    public void Utf8WithoutBom_FindsLineMatches()
    {
        var path = WriteFile("u8.txt", "the quick fox\nover the lazy dog\nfox again here\n"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Encoding, Is.InstanceOf<UTF8Encoding>());
        Assert.That(result.ContentMatches.Count, Is.EqualTo(2));
        Assert.That(result.ContentMatches[0].LineNumber, Is.EqualTo(1));
        Assert.That(result.ContentMatches[0].Column, Is.EqualTo(10));
        Assert.That(result.ContentMatches[1].LineNumber, Is.EqualTo(3));
        Assert.That(result.ContentMatches[1].Column, Is.EqualTo(0));
    }

    [Test]
    public void Utf8WithBom_StripsBomFromContent()
    {
        var path = WriteText("u8bom.txt", "fox\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Single().Column, Is.EqualTo(0));
    }

    [Test]
    public void Utf16Le_DecodesAndMatches()
    {
        var path = WriteText("u16le.txt", "hello fox\nbye fox\n", Encoding.Unicode);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Encoding!.CodePage, Is.EqualTo(Encoding.Unicode.CodePage));
        Assert.That(result.ContentMatches.Count, Is.EqualTo(2));
        Assert.That(result.ContentMatches[0].LineNumber, Is.EqualTo(1));
        Assert.That(result.ContentMatches[0].Column, Is.EqualTo(6));
        Assert.That(result.ContentMatches[1].LineNumber, Is.EqualTo(2));
        Assert.That(result.ContentMatches[1].Column, Is.EqualTo(4));
    }

    [Test]
    public void Utf16Be_DecodesAndMatches()
    {
        var path = WriteText("u16be.txt", "fox here\n", Encoding.BigEndianUnicode);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Single().Column, Is.EqualTo(0));
    }

    [Test]
    public void CrlfLineEndings_DontLeakCarriageReturnIntoLineText()
    {
        var path = WriteFile("crlf.txt", "fox\r\nfox\r\n"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(2));
        foreach (var match in result.ContentMatches)
            Assert.That(match.LineText, Is.EqualTo("fox"));
    }

    [Test]
    public void TrailingLineWithoutNewline_IsStillSearched()
    {
        var path = WriteFile("noeol.txt", "first\nsecond fox"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        var hit = result!.ContentMatches.Single();
        Assert.That(hit.LineNumber, Is.EqualTo(2));
        Assert.That(hit.Column, Is.EqualTo(7));
    }

    [Test]
    public void MultipleMatchesOnSameLine_AreRecordedSeparately()
    {
        var path = WriteFile("multi.txt", "foo bar foo baz foo\n"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "foo", CaseSensitive = true });

        var result = _searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Select(m => m.Column), Is.EqualTo(new[] { 0, 8, 16 }));
        Assert.That(result.ContentMatches.Select(m => m.LineNumber), Is.All.EqualTo(1));
    }

    [Test]
    public void RegexPattern_AppliedPerLine()
    {
        var path = WriteFile("re.txt", "log id=42 ok\nlog id=7 fail\n"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions
        {
            Pattern = @"id=\d+",
            UseRegex = true,
            CaseSensitive = true,
        });

        var result = _searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(2));
        Assert.That(result.ContentMatches[0].Length, Is.EqualTo(5));
        Assert.That(result.ContentMatches[1].Length, Is.EqualTo(4));
    }

    [Test]
    public void NonExistentFile_ReturnsNull()
    {
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "x" });
        Assert.That(_searcher.Search(Path.Combine(_tempDir, "nope.txt"), matcher), Is.Null);
    }

    [Test]
    public void LineTextDoesNotIncludeNewline()
    {
        var path = WriteFile("lt.txt", "one fox\ntwo\n"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher);

        Assert.That(result!.ContentMatches.Single().LineText, Is.EqualTo("one fox"));
    }
}
