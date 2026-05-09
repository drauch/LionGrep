using System.Text;
using NUnit.Framework;

namespace Locate.Core.Tests;

/// <summary>
/// Targeted tests for the streaming line-by-line replace path (files &gt; 4 MiB). Each fixture is
/// just-barely above the in-memory threshold so the streaming code is actually exercised, but we
/// don't need to write multi-GiB files for the boundary cases — the same buffer-carry logic runs
/// at any size above the threshold.
/// </summary>
public class FileReplacerStreamingTests
{
    private string _tempDir = string.Empty;
    private FileReplacer _replacer = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "locate-streamrep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _replacer = new FileReplacer();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ReplacementContext Ctx(string pattern, string replacement, bool keepFileDate = false, bool createBackup = false) =>
        new(new SearchOptions { Pattern = pattern, CaseSensitive = true }, replacement,
            PreserveCase: false, KeepFileDate: keepFileDate, CreateBackup: createBackup);

    /// <summary>Writes a file just over the in-memory threshold, made up of <paramref name="line"/>
    /// repeated until the size exceeds the cap.</summary>
    private string WriteOverThreshold(string name, string line, Encoding? encoding = null)
    {
        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var path = Path.Combine(_tempDir, name);
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0) fs.Write(preamble);

        var lineBytes = encoding.GetBytes(line);
        long written = 0;
        while (written <= FileReplacer.InMemoryThresholdBytes + 8 * 1024)
        {
            fs.Write(lineBytes);
            written += lineBytes.Length;
        }
        return path;
    }

    // ---- core streaming behaviour ----

    [Test]
    public void StreamingPath_ReplacesEveryOccurrence()
    {
        var path = WriteOverThreshold("repeat.txt", "alpha needle omega\n");
        var lineCountBefore = File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        var result = _replacer.Replace(path, Ctx("needle", "X"));

        Assert.That(result.ReplacementCount, Is.EqualTo(lineCountBefore));
        var rewritten = File.ReadAllText(path);
        Assert.That(rewritten, Does.Not.Contain("needle"));
        Assert.That(rewritten.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(lineCountBefore));
    }

    [Test]
    public void StreamingPath_PreservesLineEndings_LfOnly()
    {
        var path = WriteOverThreshold("lf.txt", "alpha needle omega\n");
        _replacer.Replace(path, Ctx("needle", "X"));

        var bytes = File.ReadAllBytes(path);
        Assert.That(bytes, Has.No.Member((byte)'\r'),
            "LF-only input must NOT gain any \\r bytes after streaming replace");
    }

    [Test]
    public void StreamingPath_PreservesLineEndings_CrlfOnly()
    {
        var path = WriteOverThreshold("crlf.txt", "alpha needle omega\r\n");
        _replacer.Replace(path, Ctx("needle", "X"));

        var bytes = File.ReadAllBytes(path);
        // Every '\n' must be preceded by '\r' (no bare LFs introduced).
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
                Assert.That(i > 0 && bytes[i - 1] == (byte)'\r',
                    $"bare \\n at byte {i} — CRLF preservation broken");
        }
    }

    [Test]
    public void StreamingPath_PreservesLineEndings_Mixed()
    {
        // Build a file that mixes CRLF and LF line endings, just above the threshold. The output
        // must keep each line's terminator exactly as-is.
        var path = Path.Combine(_tempDir, "mixed.txt");
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            var crlf = "alpha needle omega\r\n"u8.ToArray();
            var lf = "beta needle gamma\n"u8.ToArray();
            long written = 0;
            var toggle = false;
            while (written <= FileReplacer.InMemoryThresholdBytes + 4096)
            {
                var pick = toggle ? lf : crlf;
                fs.Write(pick);
                written += pick.Length;
                toggle = !toggle;
            }
        }

        var beforeBytes = File.ReadAllBytes(path);
        var beforeCrlfCount = CountSubsequence(beforeBytes, "\r\n"u8);
        var beforeBareLfCount = CountBareLf(beforeBytes);

        _replacer.Replace(path, Ctx("needle", "X"));

        var afterBytes = File.ReadAllBytes(path);
        Assert.That(CountSubsequence(afterBytes, "\r\n"u8), Is.EqualTo(beforeCrlfCount),
            "CRLF count must match before/after");
        Assert.That(CountBareLf(afterBytes), Is.EqualTo(beforeBareLfCount),
            "bare LF count must match before/after");
    }

    [Test]
    public void StreamingPath_HandlesCrLfStraddlingBufferBoundary()
    {
        // The streaming buffer is 64 Ki chars. Pad the file so a CRLF crosses the boundary —
        // i.e. the \r is the last char of one Read and the \n is the first char of the next.
        // Buffer size for StreamReader.Read in our impl is 65536 chars; we want a \r at index 65535
        // of the first read with the following \n at index 0 of the second read.
        const int bufSize = 65536;
        var path = Path.Combine(_tempDir, "straddle.txt");

        // Build content where:
        //   - The first 65535 chars are "x" except the last is '\r'.
        //   - The next char is '\n', then more lines.
        var sb = new StringBuilder();
        sb.Append('x', bufSize - 1);
        sb[^1] = '\r';   // char at index 65534 is now '\r' (1-indexed: 65535th char). Wait that's wrong.
        // Recompute: we want '\r' at index bufSize-1 (== 65535). That's actually past the array.
        // Reset:
        sb.Clear();
        sb.Append('x', bufSize - 1);  // 65535 'x's at indices 0..65534
        sb.Append('\r');              // index 65535
        // Now append the \n that lands in the next read:
        sb.Append('\n');
        // Then enough content to push us above the streaming threshold.
        var lineToken = "alpha needle omega\n";
        long written = sb.Length;
        while (written <= FileReplacer.InMemoryThresholdBytes + 4096)
        {
            sb.Append(lineToken);
            written += lineToken.Length;
        }

        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(sb.ToString()));

        var beforeBareLf = CountBareLf(File.ReadAllBytes(path));
        var beforeCrlf = CountSubsequence(File.ReadAllBytes(path), "\r\n"u8);
        Assume.That(beforeCrlf, Is.GreaterThan(0), "fixture must contain at least one CRLF");

        _replacer.Replace(path, Ctx("needle", "X"));

        var afterBytes = File.ReadAllBytes(path);
        Assert.That(CountSubsequence(afterBytes, "\r\n"u8), Is.EqualTo(beforeCrlf),
            "CRLF that straddles the read boundary must round-trip intact");
        Assert.That(CountBareLf(afterBytes), Is.EqualTo(beforeBareLf),
            "bare-LF count must not change");
    }

    // ---- BOM round-trip ----

    [Test]
    public void StreamingPath_RoundTripsUtf8Bom()
    {
        var path = WriteOverThreshold("bom-u8.txt", "alpha needle omega\n", encoding: Encoding.UTF8);
        Assume.That(File.ReadAllBytes(path).AsSpan(0, 3).SequenceEqual(Encoding.UTF8.GetPreamble()),
            "fixture must start with a UTF-8 BOM");

        _replacer.Replace(path, Ctx("needle", "X"));

        var afterBytes = File.ReadAllBytes(path);
        Assert.That(afterBytes.AsSpan(0, 3).SequenceEqual(Encoding.UTF8.GetPreamble()), Is.True,
            "BOM must survive the streamed rewrite");
        var afterText = Encoding.UTF8.GetString(afterBytes);
        Assert.That(afterText, Does.Not.Contain("needle"));
        Assert.That(afterText, Does.Contain("X"));
    }

    [Test]
    public void StreamingPath_RoundTripsUtf16LeBom()
    {
        var path = WriteOverThreshold("bom-u16le.txt", "alpha needle omega\n", encoding: Encoding.Unicode);
        Assume.That(File.ReadAllBytes(path).AsSpan(0, 2).SequenceEqual(Encoding.Unicode.GetPreamble()),
            "fixture must start with a UTF-16 LE BOM");

        _replacer.Replace(path, Ctx("needle", "X"));

        var afterBytes = File.ReadAllBytes(path);
        Assert.That(afterBytes.AsSpan(0, 2).SequenceEqual(Encoding.Unicode.GetPreamble()), Is.True,
            "UTF-16 LE BOM must survive the streamed rewrite");
        var afterText = Encoding.Unicode.GetString(afterBytes.AsSpan(2));
        Assert.That(afterText, Does.Not.Contain("needle"));
    }

    [Test]
    public void StreamingPath_NoBom_StaysWithoutBom()
    {
        var path = WriteOverThreshold("nobom.txt", "alpha needle omega\n");
        // WriteOverThreshold uses UTF8 without identifier — first bytes should NOT be a BOM.
        Assume.That(File.ReadAllBytes(path)[0], Is.Not.EqualTo((byte)0xEF),
            "fixture must NOT start with a BOM");

        _replacer.Replace(path, Ctx("needle", "X"));

        var afterBytes = File.ReadAllBytes(path);
        Assert.That(afterBytes[0], Is.Not.EqualTo((byte)0xEF),
            "rewritten file must NOT have gained a BOM");
    }

    // ---- count==0 path ----

    [Test]
    public void StreamingPath_NoMatches_LeavesOriginalUntouched()
    {
        var path = WriteOverThreshold("nomatch.txt", "alpha boring omega\n");
        var beforeBytes = File.ReadAllBytes(path);
        var beforeMtime = File.GetLastWriteTimeUtc(path);

        var result = _replacer.Replace(path, Ctx("needle", "X"));

        Assert.That(result.ReplacementCount, Is.EqualTo(0));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(beforeBytes),
            "no replacements → original bytes must survive byte-for-byte");
        Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(beforeMtime),
            "no replacements → mtime must not change");

        // No orphan temp files should be left in the directory.
        var tempLeftovers = Directory.EnumerateFiles(_tempDir, ".*.tmp").ToArray();
        Assert.That(tempLeftovers, Is.Empty, "no temp files should remain when count == 0");
    }

    // ---- KeepFileDate ----

    [Test]
    public void StreamingPath_KeepFileDate_RestoresOriginalMtime()
    {
        var path = WriteOverThreshold("kd.txt", "alpha needle omega\n");
        var originalMtime = new DateTime(2020, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, originalMtime);

        _replacer.Replace(path, Ctx("needle", "X", keepFileDate: true));

        Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(originalMtime).Within(TimeSpan.FromSeconds(2)));
    }

    // ---- backup ----

    [Test]
    public void StreamingPath_CreateBackup_WritesBakWithOriginalContent()
    {
        var path = WriteOverThreshold("bak.txt", "alpha needle omega\n");
        var originalBytes = File.ReadAllBytes(path);

        var result = _replacer.Replace(path, Ctx("needle", "X", createBackup: true));

        Assert.That(result.BackupPath, Is.EqualTo(path + ".bak"));
        Assert.That(File.ReadAllBytes(result.BackupPath!), Is.EqualTo(originalBytes),
            ".bak must hold the pre-replace content byte-for-byte");
    }

    // ---- cancellation ----

    [Test]
    public void StreamingPath_PreCancelled_ThrowsAndLeavesNoTemp()
    {
        var path = WriteOverThreshold("cancel.txt", "alpha needle omega\n");
        var beforeBytes = File.ReadAllBytes(path);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(() => _replacer.Replace(path, Ctx("needle", "X"), cts.Token),
            Throws.InstanceOf<OperationCanceledException>());

        Assert.That(File.ReadAllBytes(path), Is.EqualTo(beforeBytes),
            "cancellation must leave the original file untouched");
        var tempLeftovers = Directory.EnumerateFiles(_tempDir, ".*.tmp").ToArray();
        Assert.That(tempLeftovers, Is.Empty,
            "cancellation must clean up the partially-written temp file");
    }

    // ---- multiline cap ----

    [Test]
    public void DotMatchesNewline_WithinCap_StillWorks()
    {
        // Multiline regex stays in-memory but its cap is now much larger. A small file should still
        // round-trip through the multiline path correctly.
        var path = Path.Combine(_tempDir, "ml.txt");
        File.WriteAllText(path, "first line\nsecond needle line\nthird line\n");

        var result = _replacer.Replace(path, new ReplacementContext(
            new SearchOptions { Pattern = "needle", UseRegex = true, DotMatchesNewline = true, CaseSensitive = true },
            "X"));

        Assert.That(result.ReplacementCount, Is.EqualTo(1));
        Assert.That(File.ReadAllText(path), Is.EqualTo("first line\nsecond X line\nthird line\n"));
    }

    // ---- equivalence with in-memory path ----

    [Test]
    public void StreamingPath_ProducesIdenticalOutputToInMemoryPath_ForBorderlineFile()
    {
        // Generate identical content twice — once just under the threshold (in-memory path), once
        // just over (streaming path). Compare outputs byte-for-byte after replace.
        var line = "alpha needle bravo charlie delta echo foxtrot golf hotel india juliet\n";
        var lineBytes = Encoding.UTF8.GetByteCount(line);

        var smallPath = Path.Combine(_tempDir, "small.txt");
        var largePath = Path.Combine(_tempDir, "large.txt");

        // Small: a few KB, well under threshold — but the SAME content per line.
        using (var sw = new StreamWriter(smallPath, append: false, new UTF8Encoding(false)))
            for (var i = 0; i < 500; i++) sw.Write(line);

        // Large: write the SAME 500 lines but with enough additional repeats to cross the threshold.
        using (var sw = new StreamWriter(largePath, append: false, new UTF8Encoding(false)))
        {
            long written = 0;
            while (written <= FileReplacer.InMemoryThresholdBytes + 4096)
            {
                sw.Write(line);
                written += lineBytes;
            }
        }

        // Replace both with identical context.
        _replacer.Replace(smallPath, Ctx("needle", "X"));
        _replacer.Replace(largePath, Ctx("needle", "X"));

        // The first 500 lines of both should match exactly. Read just that prefix from large.
        var smallText = File.ReadAllText(smallPath);
        var largePrefix = File.ReadAllBytes(largePath).Take(smallText.Length).ToArray();
        Assert.That(Encoding.UTF8.GetString(largePrefix), Is.EqualTo(smallText),
            "streaming path must yield byte-identical output to the in-memory path for the same content");
    }

    // ---- helpers ----

    private static int CountSubsequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        var count = 0;
        var idx = 0;
        while (true)
        {
            var found = haystack[idx..].IndexOf(needle);
            if (found < 0) return count;
            count++;
            idx += found + needle.Length;
        }
    }

    private static int CountBareLf(ReadOnlySpan<byte> haystack)
    {
        var count = 0;
        for (var i = 0; i < haystack.Length; i++)
        {
            if (haystack[i] == (byte)'\n' && (i == 0 || haystack[i - 1] != (byte)'\r'))
                count++;
        }
        return count;
    }
}
