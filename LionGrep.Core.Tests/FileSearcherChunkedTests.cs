using System.Text;
using NUnit.Framework;

namespace LionGrep.Core.Tests;

/// <summary>
/// Exercises the chunked-search path used for files larger than the single-span threshold (default
/// 2 GiB). The tests use the <see cref="FileSearcher.LargeFileThresholdOverride"/> /
/// <see cref="FileSearcher.ChunkBytesOverride"/> seams to force any small file through the chunked
/// path, so the chunk-boundary edge cases can be hit on KB-sized fixtures instead of multi-GiB ones.
/// </summary>
public class FileSearcherChunkedTests
{
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "liongrep-chunked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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

    /// <summary>
    /// Build a searcher that forces <i>any</i> file larger than <paramref name="threshold"/> bytes
    /// onto the chunked path, with the chunk size set to <paramref name="chunkSize"/>. This is the
    /// only way to exercise multi-chunk behaviour without writing genuinely huge fixtures.
    /// </summary>
    private static FileSearcher Chunked(int threshold = 100, int chunkSize = 64) =>
        new() { LargeFileThresholdOverride = threshold, ChunkBytesOverride = chunkSize };

    // ---- single-chunk cases (file > threshold but ≤ chunkSize) ----

    [Test]
    public void SingleChunk_FindsAllMatches()
    {
        // 6 lines, each "fox" matches once. Whole file is < chunkSize so processed as one chunk.
        var content = "fox 1\nfox 2\nfox 3\nfox 4\nfox 5\nfox 6\n";
        var path = Write("a.txt", Encoding.UTF8.GetBytes(content));
        var searcher = Chunked(threshold: 10, chunkSize: 4096);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(6));
        Assert.That(result.ContentMatches[0].LineNumber, Is.EqualTo(1));
        Assert.That(result.ContentMatches[5].LineNumber, Is.EqualTo(6));
    }

    // ---- multi-chunk cases ----

    [Test]
    public void MultipleChunks_LineNumbersContinueAcrossBoundaries()
    {
        // 200 lines × ~14 bytes each ≈ 2800 bytes. Chunk size 256 → ~11 chunks.
        var sb = new StringBuilder();
        for (var i = 1; i <= 200; i++)
            sb.Append($"line {i:D3} fox\n");  // 14 bytes per line
        var path = Write("multi.txt", Encoding.UTF8.GetBytes(sb.ToString()));
        var searcher = Chunked(threshold: 500, chunkSize: 256);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(200));
        for (var i = 0; i < 200; i++)
        {
            Assert.That(result.ContentMatches[i].LineNumber, Is.EqualTo(i + 1),
                $"hit #{i} should be on line {i + 1}");
        }
    }

    [Test]
    public void MatchExactlyAtChunkBoundary_StillFound()
    {
        // Pad to push the match right up against the chunk-end newline.
        // Layout: 60 bytes of padding + "fox\n" + repeats. Chunk size 64 means the first chunk's
        // last newline is the "fox\n"'s newline at offset 63; the chunk ends inclusive of that newline,
        // so "fox" lives entirely in chunk 0. The next chunk starts at byte 64 with "more\n".
        var pad = new string('a', 60);  // 60 bytes
        var content = pad + "fox\nmore content here in chunk 2\nfox again\n";
        var path = Write("boundary.txt", Encoding.UTF8.GetBytes(content));
        var searcher = Chunked(threshold: 32, chunkSize: 64);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(2));
        Assert.That(result.ContentMatches[0].LineNumber, Is.EqualTo(1));
        Assert.That(result.ContentMatches[1].LineNumber, Is.EqualTo(3));
    }

    [Test]
    public void LineThatWouldStraddleBoundary_ChunkSlidesBack_ToLastNewlineInChunk()
    {
        // The trick: build content where a naive "split at byte N" would cut a line in half. The
        // chunked path slides back to the last \n in the chunk window, so the split happens between
        // lines, not within them.
        //
        // Layout (chunk size 32):
        //   bytes 0..9   "AAAAAAAAA\n"  (line 1)
        //   bytes 10..19 "BBBBBBBBB\n"  (line 2)
        //   bytes 20..49 "fox in this very long line..\n"   (line 3, spans the naive byte-32 split)
        //   bytes 50..59 "tail fox\n"   (line 4)
        // First chunk window is bytes [0..32); naive split would land in the middle of line 3.
        // We expect: chunk 0 ends at byte 20 (last \n in window), chunk 1 contains line 3 + line 4.
        var content =
            "AAAAAAAAA\n" +
            "BBBBBBBBB\n" +
            "fox in this very long line..\n" +
            "tail fox\n";
        var path = Write("straddle.txt", Encoding.UTF8.GetBytes(content));
        var searcher = Chunked(threshold: 16, chunkSize: 32);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(2));
        Assert.That(result.ContentMatches[0].LineNumber, Is.EqualTo(3));
        Assert.That(result.ContentMatches[0].LineText, Is.EqualTo("fox in this very long line.."));
        Assert.That(result.ContentMatches[1].LineNumber, Is.EqualTo(4));
        Assert.That(result.ContentMatches[1].LineText, Is.EqualTo("tail fox"));
    }

    [Test]
    public void TrailingNonTerminatedLine_IsProcessedInFinalChunk()
    {
        // No trailing \n on the last line. The final chunk's "isFinalChunk" branch processes it
        // as the last line of the file.
        var content =
            "line1 fox\n" +
            "line2 fox\n" +
            "line3 fox";  // no final \n
        var path = Write("notrail.txt", Encoding.UTF8.GetBytes(content));
        var searcher = Chunked(threshold: 8, chunkSize: 12);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(3));
        Assert.That(result.ContentMatches[2].LineNumber, Is.EqualTo(3));
        Assert.That(result.ContentMatches[2].LineText, Is.EqualTo("line3 fox"));
    }

    [Test]
    public void CaseInsensitiveAsciiLiteral_WorksAcrossChunks()
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= 50; i++)
            sb.Append($"Mixed Case FOX {i:D2}\n");  // varies between FOX/Fox/fox
        var path = Write("ci.txt", Encoding.UTF8.GetBytes(sb.ToString()));
        var searcher = Chunked(threshold: 100, chunkSize: 128);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = false });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(50));
    }

    [Test]
    public void Utf8Bom_StrippedOnce_AtStartOfFirstChunk()
    {
        // BOM detection runs on a 4-byte head view, then the chunked walk starts at offset 3 (BOM
        // length). Subsequent chunks must NOT re-treat any bytes as a BOM.
        var bom = Encoding.UTF8.GetPreamble();   // 0xEF 0xBB 0xBF
        var body = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("fox match here\n", 30)));
        var bytes = bom.Concat(body).ToArray();
        var path = Write("bom.txt", bytes);
        var searcher = Chunked(threshold: 32, chunkSize: 64);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(30));
        Assert.That(result.Encoding, Is.InstanceOf<UTF8Encoding>());
    }

    [Test]
    public void NonAsciiUtf8Pattern_RoundTripsThroughChunkedPath()
    {
        // 5-char pattern ("Größe") encodes to 6 UTF-8 bytes. Verifies the byte fast path is correct
        // for multi-byte patterns when running chunked, and that PatternCharCount (5, not 6) is
        // reported as the highlight length.
        var sb = new StringBuilder();
        for (var i = 0; i < 40; i++)
            sb.Append($"the Größe is {i}\n");
        var path = Write("nonascii.txt", Encoding.UTF8.GetBytes(sb.ToString()));
        var searcher = Chunked(threshold: 50, chunkSize: 96);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "Größe", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(40));
        foreach (var m in result.ContentMatches)
            Assert.That(m.Length, Is.EqualTo(5), "highlight length must be char count, not byte count");
    }

    [Test]
    public void RegexWithRequiredLiteral_PrefilterShortCircuitsPerChunk()
    {
        // Pattern requires literal "alpha". File has 50 lines, only line 25 contains "alpha".
        // Pre-filter should reject every chunk except the one containing line 25, but the visible
        // outcome is just: one match on line 25.
        var sb = new StringBuilder();
        for (var i = 1; i <= 50; i++)
            sb.Append(i == 25 ? $"line {i:D2} alphabeta foo\n" : $"line {i:D2} foo bar baz\n");
        var path = Write("regex.txt", Encoding.UTF8.GetBytes(sb.ToString()));
        var searcher = Chunked(threshold: 100, chunkSize: 128);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = @"alpha\w+", UseRegex = true, CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(1));
        Assert.That(result.ContentMatches[0].LineNumber, Is.EqualTo(25));
        Assert.That(result.ContentMatches[0].LineText, Does.Contain("alphabeta"));
    }

    [Test]
    public void SkipBinary_OnUtf8WithNul_StillRejectsInChunkedPath()
    {
        var bytes = "fox\0jumps\nfox\n"u8.ToArray();
        var path = Write("blob.bin", bytes);
        var searcher = Chunked(threshold: 4, chunkSize: 16);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher, skipBinary: true);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Utf16File_OverThreshold_Throws()
    {
        // UTF-16 over 2 GiB isn't supported by the chunked path (would need code-unit-aware chunking).
        // Force any UTF-16 file through the chunked path by lowering the threshold.
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("fox\nfox\n"))
            .ToArray();
        var path = Write("u16.txt", bytes);
        var searcher = Chunked(threshold: 4, chunkSize: 32);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        Assert.That(() => searcher.Search(path, matcher), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void DotMatchesNewline_OverThreshold_Throws()
    {
        // Multiline regex requires the whole file in one .NET string — incompatible with the chunked
        // walk. We surface a clear NotSupportedException rather than producing stitched-together
        // (and potentially wrong) results.
        var content = string.Concat(Enumerable.Repeat("foo\n", 50));
        var path = Write("ml.txt", Encoding.UTF8.GetBytes(content));
        var searcher = Chunked(threshold: 32, chunkSize: 64);
        var matcher = MatcherFactory.Create(new SearchOptions
        {
            Pattern = "f.+o",
            UseRegex = true,
            DotMatchesNewline = true,
            CaseSensitive = true,
        });

        Assert.That(() => searcher.Search(path, matcher, skipBinary: false, multilineMode: true, out _),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void LineLongerThanChunkSize_Throws()
    {
        // Pathological case: a single line longer than the chunk window. We don't try to grow the
        // window indefinitely; instead we surface a clear error.
        var content = new string('x', 200) + "\nfox\n";
        var path = Write("long.txt", Encoding.UTF8.GetBytes(content));
        var searcher = Chunked(threshold: 16, chunkSize: 64);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        Assert.That(() => searcher.Search(path, matcher), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void LineEqualToChunkSize_EndingWithNewline_IsAccepted()
    {
        // Edge: a single line that exactly fills the chunk window (including its terminating \n)
        // — the lastIndexOf finds the \n at the chunk's last byte and processBytes == chunkSize.
        // Should not throw and should still find subsequent matches.
        var line = new string('a', 63);  // 63 'a's + \n = 64 bytes
        var content = line + "\nfox\n";
        var path = Write("exact.txt", Encoding.UTF8.GetBytes(content));
        var searcher = Chunked(threshold: 16, chunkSize: 64);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(1));
        Assert.That(result.ContentMatches[0].LineNumber, Is.EqualTo(2));
    }

    [Test]
    public void DenseMatchesOnSameLine_LineTextSharedAcrossHits()
    {
        // The byte-fast-path's per-line cache survives chunk boundaries (each chunk has its own
        // cache, but a hot line within one chunk should still share a single string reference).
        var line = "fox fox fox fox fox\n";  // 5 hits on line 1
        var path = Write("dense.txt", Encoding.UTF8.GetBytes(line + "tail\n"));
        var searcher = Chunked(threshold: 8, chunkSize: 64);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(5));
        // All 5 LineMatch records for line 1 must reference the same string instance.
        var first = result.ContentMatches[0].LineText;
        for (var i = 1; i < 5; i++)
            Assert.That(ReferenceEquals(result.ContentMatches[i].LineText, first), Is.True,
                $"hit {i}'s LineText should be the same string instance as hit 0's");
    }

    [Test]
    public void WholeWord_RespectedAcrossChunks()
    {
        // "fox" is a whole word on lines 1, 3, 5; a substring of "foxes" on lines 2, 4.
        var content =
            "the fox\n" +
            "the foxes\n" +
            "fox alone\n" +
            "many foxes here\n" +
            "fox\n";
        var path = Write("ww.txt", Encoding.UTF8.GetBytes(content));
        var searcher = Chunked(threshold: 8, chunkSize: 16);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true, WholeWord = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(3));
        Assert.That(result.ContentMatches.Select(m => m.LineNumber), Is.EqualTo(new[] { 1, 3, 5 }));
    }

    [Test]
    public void NoMatches_ReturnsNull_EvenAfterIteratingAllChunks()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 100; i++)
            sb.Append("nothing useful here\n");
        var path = Write("none.txt", Encoding.UTF8.GetBytes(sb.ToString()));
        var searcher = Chunked(threshold: 64, chunkSize: 128);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Cancellation_StopsTheChunkedWalk()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 1000; i++)
            sb.Append($"line {i:D5} fox match here\n");
        var path = Write("cancel.txt", Encoding.UTF8.GetBytes(sb.ToString()));
        var searcher = Chunked(threshold: 100, chunkSize: 128);
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = "fox", CaseSensitive = true });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(() => searcher.Search(path, matcher, ct: cts.Token),
            Throws.InstanceOf<OperationCanceledException>(),
            "A pre-cancelled token must short-circuit the chunked loop's ThrowIfCancellationRequested.");
    }

    // ---- "real" multi-GiB test: opt-in via [Category("Slow")] so CI doesn't always pay 2.5 GB of writes ----

    [Test]
    [Category("Slow")]
    [Explicit("Allocates a 2.5 GiB scratch file. Run manually before tagging a release.")]
    public void RealMultiGiBFile_FindsMatchAtKnownOffset()
    {
        // Build a 2.5 GiB file by repeating a 1 MiB chunk. The very last chunk contains a known
        // unique needle. This exercises the genuine > int.MaxValue path with default chunk sizes.
        const long targetSize = 2_600_000_000L; // ~2.42 GiB, comfortably > int.MaxValue
        const string needle = "TheQuickBrownFoxNeedle9183";

        var path = Path.Combine(_tempDir, "huge.txt");
        var oneMiB = new byte[1024 * 1024];
        // Fill with predictable text + newlines so chunk-boundary alignment is exercised.
        var line = "abcdefghijklmnopqrstuvwxyz0123456789 padding text padding text padding text\n"u8.ToArray();
        for (var i = 0; i < oneMiB.Length;)
        {
            var copyLen = Math.Min(line.Length, oneMiB.Length - i);
            line.AsSpan(0, copyLen).CopyTo(oneMiB.AsSpan(i, copyLen));
            i += copyLen;
        }

        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
        {
            long written = 0;
            while (written + oneMiB.Length < targetSize)
            {
                fs.Write(oneMiB);
                written += oneMiB.Length;
            }
            // Final chunk: write a line containing the unique needle, then more padding to hit targetSize.
            var needleLine = Encoding.UTF8.GetBytes($"finalSection {needle} on this line.\n");
            fs.Write(needleLine);
            written += needleLine.Length;
            // Pad to targetSize so the needle isn't the absolute last bytes — exercises mid-final-chunk.
            while (written < targetSize)
            {
                var remaining = (int)Math.Min(oneMiB.Length, targetSize - written);
                fs.Write(oneMiB, 0, remaining);
                written += remaining;
            }
        }

        Assume.That(new FileInfo(path).Length, Is.GreaterThan(int.MaxValue),
            "The fixture must be larger than 2 GiB to exercise the chunked path.");

        var searcher = new FileSearcher();  // default thresholds; real chunked path
        var matcher = MatcherFactory.Create(new SearchOptions { Pattern = needle, CaseSensitive = true });

        var result = searcher.Search(path, matcher);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ContentMatches.Count, Is.EqualTo(1));
        Assert.That(result.ContentMatches[0].LineText, Does.Contain(needle));
    }
}
