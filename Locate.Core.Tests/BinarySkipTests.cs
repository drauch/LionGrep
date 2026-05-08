using NUnit.Framework;

namespace Locate.Core.Tests;

public class BinarySkipTests
{
    private string _tempDir = string.Empty;
    private FileSearcher _searcher = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "locate-binskip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _searcher = new FileSearcher();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Test]
    public void SkipBinary_OnUtf8WithNul_ReturnsNull()
    {
        var path = Write("blob.bin", "fox\0jumps\nfox\n"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher, skipBinary: true);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void SkipBinary_Off_StillSearchesBinary()
    {
        var path = Write("blob.bin", "fox\0jumps\nfox\n"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher, skipBinary: false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches, Is.Not.Empty);
    }

    [Test]
    public void SkipBinary_OnTextFile_StillReturnsMatches()
    {
        var path = Write("a.txt", "hello fox\n"u8.ToArray());
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher, skipBinary: true);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(1));
    }

    [Test]
    public void SkipBinary_OnUtf16Le_StillSearches()
    {
        // UTF-16 LE has NUL bytes from ASCII chars; binary detection must NOT flag it.
        var bytes = System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes("fox\nfox\n"))
            .ToArray();
        var path = Write("u16.txt", bytes);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = _searcher.Search(path, matcher, skipBinary: true);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(2));
    }
}
