using System.Text;
using NUnit.Framework;

namespace LionGrep.Core.Tests;

public class FileReplacerTests
{
    private string _tempDir = string.Empty;
    private FileReplacer _replacer = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "liongrep-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _replacer = new FileReplacer();
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

    private static ReplacementContext Ctx(string pattern, string replacement, bool useRegex = false, bool preserveCase = false, bool keepFileDate = false, bool wholeWord = false, bool caseSensitive = true) =>
        new(new SearchOptions
        {
            Pattern = pattern,
            UseRegex = useRegex,
            WholeWord = wholeWord,
            CaseSensitive = caseSensitive,
        }, replacement, preserveCase, keepFileDate);

    [Test]
    public void NoMatches_FileUnchanged_AndZeroCount()
    {
        var path = Write("a.txt", "no match here\n"u8.ToArray());
        var beforeMtime = File.GetLastWriteTimeUtc(path);

        var result = _replacer.Replace(path, Ctx("missing", "X"));

        Assert.That(result.ReplacementCount, Is.EqualTo(0));
        Assert.That(File.ReadAllText(path), Is.EqualTo("no match here\n"));
        Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(beforeMtime));
    }

    [Test]
    public void EmptyFile_NoOp()
    {
        var path = Write("empty.txt", []);
        var result = _replacer.Replace(path, Ctx("x", "y"));
        Assert.That(result.ReplacementCount, Is.EqualTo(0));
    }

    [Test]
    public void NonExistentFile_NoOp()
    {
        var result = _replacer.Replace(Path.Combine(_tempDir, "nope.txt"), Ctx("x", "y"));
        Assert.That(result.ReplacementCount, Is.EqualTo(0));
    }

    [Test]
    public void SimpleReplace_WritesNewContent()
    {
        var path = Write("a.txt", "hello fox\nbye fox\n"u8.ToArray());

        var result = _replacer.Replace(path, Ctx("fox", "cat"));

        Assert.That(result.ReplacementCount, Is.EqualTo(2));
        Assert.That(File.ReadAllText(path), Is.EqualTo("hello cat\nbye cat\n"));
    }

    [Test]
    public void CrlfLineEndings_Preserved()
    {
        var path = Write("a.txt", "fox\r\nfox\r\n"u8.ToArray());

        _replacer.Replace(path, Ctx("fox", "cat"));

        Assert.That(File.ReadAllBytes(path), Is.EqualTo("cat\r\ncat\r\n"u8.ToArray()));
    }

    [Test]
    public void MixedLineEndings_PreservedPerLine()
    {
        var path = Write("a.txt", "fox\r\nfox\nfox"u8.ToArray());

        _replacer.Replace(path, Ctx("fox", "cat"));

        Assert.That(File.ReadAllBytes(path), Is.EqualTo("cat\r\ncat\ncat"u8.ToArray()));
    }

    [Test]
    public void Utf8Bom_RoundTrips()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var path = Write("a.txt", bom.Concat("fox\n"u8.ToArray()).ToArray());

        _replacer.Replace(path, Ctx("fox", "cat"));

        var bytes = File.ReadAllBytes(path);
        Assert.That(bytes[..3], Is.EqualTo(bom));
        Assert.That(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), Is.EqualTo("cat\n"));
    }

    [Test]
    public void Utf16Le_RoundTripsWithBom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("fox\nfox\n")).ToArray();
        var path = Write("a.txt", bytes);

        _replacer.Replace(path, Ctx("fox", "cat"));

        var roundTripped = File.ReadAllBytes(path);
        Assert.That(roundTripped[..2], Is.EqualTo(Encoding.Unicode.GetPreamble()));
        Assert.That(Encoding.Unicode.GetString(roundTripped, 2, roundTripped.Length - 2), Is.EqualTo("cat\ncat\n"));
    }

    [Test]
    public void PreserveCase_Mixed_WrittenWithReshapedReplacements()
    {
        var path = Write("a.txt", "foo Foo FOO\n"u8.ToArray());

        _replacer.Replace(path, Ctx("foo", "bar", preserveCase: true, caseSensitive: false));

        Assert.That(File.ReadAllText(path), Is.EqualTo("bar Bar BAR\n"));
    }

    [Test]
    public void RegexBackref_AppliedToFile()
    {
        var path = Write("a.txt", "log id=42 ok\nlog id=7 fail\n"u8.ToArray());

        var result = _replacer.Replace(path, Ctx(@"id=(\d+)", "ID:$1", useRegex: true));

        Assert.That(result.ReplacementCount, Is.EqualTo(2));
        Assert.That(File.ReadAllText(path), Is.EqualTo("log ID:42 ok\nlog ID:7 fail\n"));
    }

    [Test]
    public void KeepFileDate_RestoresOriginalMtime()
    {
        var path = Write("a.txt", "fox\n"u8.ToArray());
        var original = DateTime.UtcNow.AddDays(-30);
        File.SetLastWriteTimeUtc(path, original);

        _replacer.Replace(path, Ctx("fox", "cat", keepFileDate: true));

        var after = File.GetLastWriteTimeUtc(path);
        Assert.That(after, Is.EqualTo(original).Within(TimeSpan.FromSeconds(2)));
        Assert.That(File.ReadAllText(path), Is.EqualTo("cat\n"));
    }

    [Test]
    public void KeepFileDateOff_BumpsMtime()
    {
        var path = Write("a.txt", "fox\n"u8.ToArray());
        var original = DateTime.UtcNow.AddDays(-30);
        File.SetLastWriteTimeUtc(path, original);

        _replacer.Replace(path, Ctx("fox", "cat", keepFileDate: false));

        Assert.That(File.GetLastWriteTimeUtc(path), Is.GreaterThan(original.AddMinutes(1)));
    }

    [Test]
    public void EmptyReplacement_DeletesMatches()
    {
        var path = Write("a.txt", "axbxc\n"u8.ToArray());

        var result = _replacer.Replace(path, Ctx("x", ""));

        Assert.That(result.ReplacementCount, Is.EqualTo(2));
        Assert.That(File.ReadAllText(path), Is.EqualTo("abc\n"));
    }

    [Test]
    public void WholeWord_OnlyReplacesWholeWords()
    {
        var path = Write("a.txt", "cat catalog cat\n"u8.ToArray());

        _replacer.Replace(path, Ctx("cat", "dog", wholeWord: true));

        Assert.That(File.ReadAllText(path), Is.EqualTo("dog catalog dog\n"));
    }

    [Test]
    public void LargeFile_AboveInMemoryThreshold_RoutesThroughStreamingPath_AndReplacesCorrectly()
    {
        // Build a > 4 MiB file by repeating "alpha needle omega\n" until we cross the threshold.
        var path = Path.Combine(_tempDir, "big.txt");
        using (var sw = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            long written = 0;
            var line = "alpha needle omega\n";
            var lineBytes = Encoding.UTF8.GetByteCount(line);
            while (written <= FileReplacer.InMemoryThresholdBytes + 1024)
            {
                sw.Write(line);
                written += lineBytes;
            }
        }
        var fileSize = new FileInfo(path).Length;
        Assume.That(fileSize, Is.GreaterThan(FileReplacer.InMemoryThresholdBytes),
            "fixture must exceed the in-memory threshold to exercise the streaming path");

        var result = _replacer.Replace(path, Ctx("needle", "X"));

        Assert.That(result.ReplacementCount, Is.GreaterThan(0));
        // Spot-check: every "needle" became "X" and total file shrank by 5 bytes per replacement.
        var rewritten = File.ReadAllText(path);
        Assert.That(rewritten, Does.Not.Contain("needle"));
        Assert.That(rewritten.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(result.ReplacementCount));
    }

    [Test]
    public void DotMatchesNewline_AboveItsCap_StillThrows()
    {
        // Multiline regex replace can't be streamed; cap is much higher (256 MiB) but still finite.
        // Build a sparse file > the cap by SetLength.
        var path = Path.Combine(_tempDir, "huge.txt");
        using (var fs = new FileStream(path, FileMode.CreateNew))
            fs.SetLength(FileReplacer.MultilineMemoryCapBytes + 1);

        var ctx = new ReplacementContext(
            new SearchOptions { Pattern = "a", UseRegex = true, DotMatchesNewline = true, CaseSensitive = true },
            "b");

        Assert.That(() => _replacer.Replace(path, ctx), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void CreateBackup_WritesBackupWithOriginalContent_AndReturnsItsPath()
    {
        var original = "alpha bravo alpha\n";
        var path = Write("a.txt", Encoding.UTF8.GetBytes(original));
        var ctx = Ctx("alpha", "X") with { CreateBackup = true };

        var result = _replacer.Replace(path, ctx);

        Assert.That(result.ReplacementCount, Is.EqualTo(2));
        Assert.That(result.BackupPath, Is.EqualTo(path + ".lgbak"));
        Assert.That(File.Exists(result.BackupPath), Is.True);
        Assert.That(File.ReadAllText(result.BackupPath!), Is.EqualTo(original));
        Assert.That(File.ReadAllText(path), Is.EqualTo("X bravo X\n"));
    }

    [Test]
    public void CreateBackup_WithNoMatches_DoesNotCreateBackupFile()
    {
        var path = Write("b.txt", "no relevant content here\n"u8.ToArray());
        var ctx = Ctx("missing", "X") with { CreateBackup = true };

        var result = _replacer.Replace(path, ctx);

        Assert.That(result.ReplacementCount, Is.EqualTo(0));
        Assert.That(result.BackupPath, Is.Null);
        Assert.That(File.Exists(path + ".lgbak"), Is.False);
    }

    [Test]
    public void CreateBackup_HonorsCustomExtension()
    {
        var original = "alpha bravo\n";
        var path = Write("ext.txt", Encoding.UTF8.GetBytes(original));
        var ctx = Ctx("alpha", "X") with { CreateBackup = true, BackupExtension = "bak" };

        var result = _replacer.Replace(path, ctx);

        Assert.That(result.BackupPath, Is.EqualTo(path + ".bak"));
        Assert.That(File.Exists(path + ".bak"), Is.True);
        Assert.That(File.Exists(path + ".lgbak"), Is.False);
    }

    [Test]
    public void DotMatchesNewline_RegexReplacesAcrossNewlines()
    {
        var input = "<a>\nfoo\nbar\n</a>\n";
        var path = Write("dmn.txt", Encoding.UTF8.GetBytes(input));
        var ctx = new ReplacementContext(
            Search: new SearchOptions { Pattern = "<a>.+?</a>", UseRegex = true, DotMatchesNewline = true },
            Replacement: "<x/>");

        var result = _replacer.Replace(path, ctx);

        Assert.That(result.ReplacementCount, Is.EqualTo(1));
        Assert.That(File.ReadAllText(path), Is.EqualTo("<x/>\n"));
    }

    [Test]
    public void DotMatchesNewline_Off_DotStaysWithinLines_OnReplace()
    {
        var input = "<a>\nfoo\n</a>\n";
        var path = Write("dmn-off.txt", Encoding.UTF8.GetBytes(input));
        var ctx = new ReplacementContext(
            Search: new SearchOptions { Pattern = "<a>.+</a>", UseRegex = true, DotMatchesNewline = false },
            Replacement: "<x/>");

        var result = _replacer.Replace(path, ctx);

        Assert.That(result.ReplacementCount, Is.EqualTo(0));
        Assert.That(File.ReadAllText(path), Is.EqualTo(input));
    }

    [Test]
    public void CreateBackup_PreservesOriginalMtime_OnTheBackupFile()
    {
        var path = Write("c.txt", "needle here\n"u8.ToArray());
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        var ctx = Ctx("needle", "X") with { CreateBackup = true };

        var result = _replacer.Replace(path, ctx);

        Assert.That(result.BackupPath, Is.Not.Null);
        // The backup's mtime should equal the pre-replace mtime so Undo can restore it.
        Assert.That(File.GetLastWriteTimeUtc(result.BackupPath!), Is.EqualTo(stamp));
    }
}
