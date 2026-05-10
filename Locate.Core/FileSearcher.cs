using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Locate.Core;

public sealed class FileSearcher
{
    private const int BinaryProbeBytes = 8192;

    /// <summary>
    /// Files smaller than this go through <c>File.ReadAllBytes</c> instead of mmap. mmap has fixed
    /// per-file setup cost (Section object creation, view mapping) that dominates for small files;
    /// ripgrep's analysis shows the same crossover. NTFS + SSD reads at this size are essentially free.
    /// </summary>
    private const int MmapMinBytes = 64 * 1024;

    /// <summary>
    /// Default chunk size for the streaming path used when a file exceeds 2 GiB. Each chunk is processed
    /// end-to-end through the same per-line search routines, with chunk boundaries forced onto newline
    /// positions so no line is ever split. 64 MiB is the sweet spot: small enough that the OS readahead
    /// pipeline stays full and a single view's address space is trivial, large enough that view-creation
    /// overhead amortises to nothing.
    /// </summary>
    private const int DefaultChunkBytes = 64 * 1024 * 1024;

    /// <summary>Files at or below this size use the single-span path (mmap or buffered read).
    /// Above it, the chunked path runs. Default is <see cref="int.MaxValue"/> because a single
    /// <c>ReadOnlySpan&lt;byte&gt;</c> can't address more than that.</summary>
    private const long DefaultLargeFileThreshold = int.MaxValue;

    /// <summary>Test seam: when &gt; 0, overrides <see cref="DefaultChunkBytes"/>. Tests use this to
    /// exercise the chunked code path on small (KB-sized) files instead of having to write multi-GiB
    /// fixtures.</summary>
    internal int ChunkBytesOverride;

    /// <summary>Test seam: when &gt; 0, overrides <see cref="DefaultLargeFileThreshold"/>. Pair with
    /// <see cref="ChunkBytesOverride"/> to force any small file through the chunked path.</summary>
    internal long LargeFileThresholdOverride;

    private int ChunkBytes => ChunkBytesOverride > 0 ? ChunkBytesOverride : DefaultChunkBytes;
    private long LargeFileThreshold => LargeFileThresholdOverride > 0 ? LargeFileThresholdOverride : DefaultLargeFileThreshold;

    public FileMatch? Search(string path, IMatcher matcher, CancellationToken ct = default, bool skipBinary = false)
        => Search(path, matcher, skipBinary, multilineMode: false, out _, ct);

    public FileMatch? Search(string path, IMatcher matcher, bool skipBinary, out bool wasBinarySkipped, CancellationToken ct = default)
        => Search(path, matcher, skipBinary, multilineMode: false, out wasBinarySkipped, ct);

    /// <param name="multilineMode">When true, the matcher receives the entire file content as one span — required for regex patterns that cross newlines (e.g. with `RegexOptions.Singleline`). When false, each line is matched independently.</param>
    public FileMatch? Search(string path, IMatcher matcher, bool skipBinary, bool multilineMode, out bool wasBinarySkipped, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(matcher);

        wasBinarySkipped = false;

        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
            return null;

        if (info.Length <= LargeFileThreshold)
            return SearchSingleSpan(path, info, matcher, skipBinary, multilineMode, ref wasBinarySkipped, ct);

        // Chunked path: only reached for files larger than the single-span threshold (2 GiB by default).
        // DotMatchesNewline (singleline) regex semantics require the whole file in one .NET string, which
        // can't be done above 2 GiB without crossing the int-indexed string boundary.
        if (multilineMode)
            throw new NotSupportedException(
                $"DotMatchesNewline mode is not supported for files larger than 2 GiB (path: {path}). " +
                "Singleline regex requires the entire file to be matched in one pass, which would " +
                "require a single .NET string spanning the whole file.");

        return SearchChunked(path, info.Length, matcher, skipBinary, ref wasBinarySkipped, ct);
    }

    /// <summary>≤ 2 GiB path: read once into a single span (mmap or buffered). Identical behaviour to v1.0.</summary>
    private static FileMatch? SearchSingleSpan(
        string path, FileInfo info, IMatcher matcher,
        bool skipBinary, bool multilineMode, ref bool wasBinarySkipped, CancellationToken ct)
    {
        // Small-file path: a buffered read is faster than mmap'ing per file because mmap setup cost
        // (Section object + view mapping + Acquire/Release ceremony) dominates at this size.
        if (info.Length < MmapMinBytes)
        {
            var bytes = File.ReadAllBytes(path);
            return SearchSpan(path, bytes, matcher, skipBinary, multilineMode, ref wasBinarySkipped, ct);
        }

        using var mmap = MemoryMappedFile.CreateFromFile(
            path, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
        using var view = mmap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        unsafe
        {
            byte* ptr = null;
            try
            {
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                var bytes = new ReadOnlySpan<byte>(ptr, (int)info.Length);
                return SearchSpan(path, bytes, matcher, skipBinary, multilineMode, ref wasBinarySkipped, ct);
            }
            finally
            {
                if (ptr is not null)
                    view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }

    /// <summary>
    /// Single dispatch point for "we have the file's bytes, now search them". Shared by the mmap path
    /// (large files) and the buffered-read path (small files). The returned <see cref="FileMatch"/>
    /// holds only decoded strings — no references to the input span — so it's safe to outlive the
    /// underlying mmap or array.
    /// </summary>
    private static FileMatch? SearchSpan(
        string path, ReadOnlySpan<byte> bytes, IMatcher matcher,
        bool skipBinary, bool multilineMode, ref bool wasBinarySkipped, CancellationToken ct)
    {
        var detected = EncodingDetection.Detect(bytes);
        var content = bytes[detected.BomLength..];

        if (skipBinary && IsLikelyBinary(content, detected.Encoding))
        {
            wasBinarySkipped = true;
            return null;
        }

        if (multilineMode)
        {
            // Decode the whole file at once so the regex sees newlines.
            var text = detected.Encoding.GetString(content);
            return SearchWholeText(path, text, detected.Encoding, matcher, ct);
        }

        List<LineMatch>? hits = null;
        SearchSlice(content, detected.Encoding, matcher, ref hits, startLineNumber: 1, ct);
        return hits is null ? null : new FileMatch(path, detected.Encoding, hits, []);
    }

    /// <summary>
    /// Streaming path for files larger than the single-span threshold. UTF-8 only (with or without BOM);
    /// other encodings are vanishingly rare at this size and would need code-unit-aware chunk alignment.
    /// Each chunk is forced to end at the last newline within a 64 MiB window so no line ever straddles
    /// a chunk boundary, then handed to the same per-slice search routine the small-file path uses —
    /// just with a running line counter carried across chunks.
    /// </summary>
    private FileMatch? SearchChunked(
        string path, long fileLength, IMatcher matcher,
        bool skipBinary, ref bool wasBinarySkipped, CancellationToken ct)
    {
        using var mmap = MemoryMappedFile.CreateFromFile(
            path, FileMode.Open, mapName: null, capacity: fileLength, MemoryMappedFileAccess.Read);

        // BOM / encoding detection: read just the first up-to-4 bytes via a tiny throwaway view.
        // Note: AcquirePointer returns a pointer to the start of the *mapped page*, not to the
        // requested offset within the file. Windows rounds view offsets down to allocation
        // granularity (typically 64 KiB) and exposes the rounded-off bytes via PointerOffset, so
        // we add that to land on the actual data. (Offset 0 is always page-aligned, so this is a
        // no-op here, but keeping the pattern uniform across all three views below.)
        DetectedEncoding detected;
        var headLength = (int)Math.Min(4L, fileLength);
        unsafe
        {
            using var headView = mmap.CreateViewAccessor(0, headLength, MemoryMappedFileAccess.Read);
            byte* hp = null;
            try
            {
                headView.SafeMemoryMappedViewHandle.AcquirePointer(ref hp);
                detected = EncodingDetection.Detect(new ReadOnlySpan<byte>(hp + headView.PointerOffset, headLength));
            }
            finally
            {
                if (hp is not null) headView.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }

        if (detected.Encoding is not UTF8Encoding)
            throw new NotSupportedException(
                $"Files larger than 2 GiB are only supported in UTF-8 (with or without BOM); detected " +
                $"{detected.Encoding.WebName} (path: {path}).");

        long offset = detected.BomLength;
        var totalLength = fileLength;

        // Binary check probes the first 8 KiB after the BOM, same window as the single-span path.
        if (skipBinary)
        {
            var probeSize = (int)Math.Min(BinaryProbeBytes, totalLength - offset);
            if (probeSize > 0)
            {
                using var probeView = mmap.CreateViewAccessor(offset, probeSize, MemoryMappedFileAccess.Read);
                unsafe
                {
                    byte* pp = null;
                    try
                    {
                        probeView.SafeMemoryMappedViewHandle.AcquirePointer(ref pp);
                        var probe = new ReadOnlySpan<byte>(pp + probeView.PointerOffset, probeSize);
                        if (probe.IndexOf((byte)0) >= 0)
                        {
                            wasBinarySkipped = true;
                            return null;
                        }
                    }
                    finally
                    {
                        if (pp is not null) probeView.SafeMemoryMappedViewHandle.ReleasePointer();
                    }
                }
            }
        }

        var chunkBytes = ChunkBytes;
        List<LineMatch>? hits = null;
        var lineNumber = 1;

        while (offset < totalLength)
        {
            ct.ThrowIfCancellationRequested();

            var remaining = totalLength - offset;
            var chunkSize = (int)Math.Min(remaining, chunkBytes);
            var isFinalChunk = remaining <= chunkBytes;

            using var view = mmap.CreateViewAccessor(offset, chunkSize, MemoryMappedFileAccess.Read);
            unsafe
            {
                byte* ptr = null;
                try
                {
                    view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                    // ptr is to the start of the mapped page (offset rounded down to allocation
                    // granularity); PointerOffset is the bytes to skip to land on our requested offset.
                    var span = new ReadOnlySpan<byte>(ptr + view.PointerOffset, chunkSize);

                    int processBytes;
                    if (isFinalChunk)
                    {
                        processBytes = chunkSize;
                    }
                    else
                    {
                        // Slide the chunk end back to the last newline so the slice is whole lines only.
                        // This guarantees each match's line is fully decodable from within the slice —
                        // no need to carry overlap bytes across boundaries.
                        var lastNl = span.LastIndexOf((byte)'\n');
                        if (lastNl < 0)
                            throw new NotSupportedException(
                                $"Found a single line longer than {chunkBytes / 1024 / 1024} MiB at offset {offset} " +
                                $"(path: {path}). The streaming search path requires at least one newline per chunk.");
                        processBytes = lastNl + 1;
                    }

                    var slice = span[..processBytes];
                    lineNumber = SearchSlice(slice, detected.Encoding, matcher, ref hits, lineNumber, ct);
                    offset += processBytes;
                }
                finally
                {
                    if (ptr is not null) view.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
        }

        return hits is null ? null : new FileMatch(path, detected.Encoding, hits, []);
    }

    /// <summary>
    /// Searches a single slice of bytes (a sequence of complete lines plus an optional trailing
    /// non-terminated line for the final chunk). Picks the byte-level fast path, regex pre-filter,
    /// or per-line decode based on the matcher and encoding. Mutates <paramref name="hits"/> in
    /// place and returns the line number at the end of the slice so the caller can resume.
    /// </summary>
    private static int SearchSlice(
        ReadOnlySpan<byte> content, Encoding encoding, IMatcher matcher,
        ref List<LineMatch>? hits, int startLineNumber, CancellationToken ct)
    {
        // Regex pre-filter. If we extracted a required literal substring from the pattern, do a single
        // SIMD-vectorized byte scan first; if the literal isn't in this slice, the regex can't match
        // anywhere in it and we skip the regex engine entirely. We still walk the slice's newlines once
        // to advance the running line counter for downstream chunks.
        if (encoding is UTF8Encoding && matcher is RegexMatcher rm && rm.RequiredLiteralUtf8 is not null)
        {
            bool found;
            if (rm.CaseSensitive)
                found = content.IndexOf(rm.RequiredLiteralUtf8) >= 0;
            else if (rm.RequiredLiteralIsAscii)
                found = IndexOfAsciiCaseInsensitive(content, rm.RequiredLiteralAsciiLower!) >= 0;
            else
                found = true;   // can't safely pre-filter case-insensitive non-ASCII; assume hit and let the regex engine decide.
            if (!found)
                return CountNewlines(content, startLineNumber);
        }

        // Byte-level fast path for literal patterns over UTF-8 content. UTF-8 is self-synchronizing,
        // so a multi-byte character's bytes only appear at that character's position — case-sensitive
        // byte-level IndexOf is correct for ANY pattern, not just ASCII. The case-insensitive variant
        // still requires ASCII (we'd need full Unicode case folding for non-ASCII), so we restrict
        // case-insensitive byte search to ASCII patterns.
        if (encoding is UTF8Encoding && matcher is LiteralMatcher lit && lit.Utf8PatternBytes.Length > 0)
        {
            if (lit.CaseSensitive)
                return SearchUtf8LiteralBytes(
                    content, encoding, lit.Utf8PatternBytes, lit.PatternCharCount,
                    ignoreCase: false, lit.WholeWord, ref hits, startLineNumber, ct);
            if (lit.IsAsciiPattern)
                return SearchUtf8LiteralBytes(
                    content, encoding, lit.AsciiLowerPatternBytes!, lit.PatternCharCount,
                    ignoreCase: true, lit.WholeWord, ref hits, startLineNumber, ct);
            // case-insensitive non-ASCII literal — needs Unicode-aware folding, fall through to per-line.
        }

        return encoding is UTF8Encoding
            ? SearchUtf8(content, encoding, matcher, ref hits, startLineNumber, ct)
            : SearchDecoded(content, encoding, matcher, ref hits, startLineNumber, ct);
    }

    private static bool IsLikelyBinary(ReadOnlySpan<byte> content, Encoding encoding)
    {
        // BOM-detected UTF-16/32 are always treated as text — they legitimately have NUL bytes from ASCII chars.
        if (encoding is not UTF8Encoding) return false;
        var probe = content[..Math.Min(content.Length, BinaryProbeBytes)];
        return probe.IndexOf((byte)0) >= 0;
    }

    /// <summary>Counts newlines in a slice and returns <paramref name="startLineNumber"/> + count.
    /// Used to keep the line counter accurate across chunks where the regex pre-filter short-circuits.</summary>
    private static int CountNewlines(ReadOnlySpan<byte> content, int startLineNumber)
    {
        var lineNumber = startLineNumber;
        var pos = 0;
        while (pos < content.Length)
        {
            var nl = content[pos..].IndexOf((byte)'\n');
            if (nl < 0) break;
            pos += nl + 1;
            lineNumber++;
        }
        return lineNumber;
    }

    private static int SearchUtf8(
        ReadOnlySpan<byte> content, Encoding encoding, IMatcher matcher,
        ref List<LineMatch>? hits, int startLineNumber, CancellationToken ct)
    {
        var spans = new List<MatchSpan>(capacity: 8);
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        var lineNumber = startLineNumber;
        try
        {
            var pos = 0;
            while (pos <= content.Length)
            {
                ct.ThrowIfCancellationRequested();

                var remainder = content[pos..];
                var nl = remainder.IndexOf((byte)'\n');
                var lineEnd = nl < 0 ? content.Length : pos + nl;
                var lineBytes = content[pos..lineEnd];
                if (lineBytes.Length > 0 && lineBytes[^1] == (byte)'\r')
                    lineBytes = lineBytes[..^1];

                // UTF-8 produces at most one char per byte, so lineBytes.Length is always a safe upper bound.
                // Skipping GetCharCount avoids a second scan of the same line.
                if (lineBytes.Length > buffer.Length)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                    buffer = ArrayPool<char>.Shared.Rent(lineBytes.Length);
                }
                var written = encoding.GetChars(lineBytes, buffer);
                var lineChars = buffer.AsSpan(0, written);

                spans.Clear();
                matcher.FindMatches(lineChars, spans);
                if (spans.Count > 0)
                {
                    hits ??= new List<LineMatch>();
                    var lineText = new string(lineChars);
                    foreach (var span in spans)
                        hits.Add(new LineMatch(lineNumber, span.Column, span.Length, lineText));
                }

                if (nl < 0)
                    break;
                pos = lineEnd + 1;
                lineNumber++;
            }
            return lineNumber;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// SIMD-driven byte scan for literal patterns over UTF-8 content. Works for any UTF-8 pattern when
    /// case-sensitive (UTF-8 is self-synchronizing); case-insensitive callers must pass pre-lowercased
    /// ASCII bytes. Skips per-line decoding entirely; only matched lines are decoded for display. Honors
    /// whole-word boundaries — word chars are themselves ASCII, so any byte ≥ 128 (i.e. part of a
    /// non-ASCII char) always counts as a non-word boundary.
    /// </summary>
    private static int SearchUtf8LiteralBytes(
        ReadOnlySpan<byte> content,
        Encoding encoding,
        byte[] patternBytes,
        int patternCharCount,
        bool ignoreCase,
        bool wholeWord,
        ref List<LineMatch>? hits,
        int startLineNumber,
        CancellationToken ct)
    {
        if (patternBytes.Length == 0) return startLineNumber;

        // Maintain (lineNumber, lineStart) incrementally so each match doesn't re-walk from byte 0.
        var lineStart = 0;
        var lineNumber = startLineNumber;

        // Cache the most recently decoded line so dense matches on a single line don't re-allocate
        // and re-decode the same string per hit.
        var cachedLineNumber = 0;
        string? cachedLineText = null;

        var pos = 0;
        while (pos <= content.Length - patternBytes.Length)
        {
            ct.ThrowIfCancellationRequested();

            var hitOffset = ignoreCase
                ? IndexOfAsciiCaseInsensitive(content[pos..], patternBytes)
                : content[pos..].IndexOf(patternBytes);
            if (hitOffset < 0) break;

            var hitStart = pos + hitOffset;
            var hitEnd = hitStart + patternBytes.Length;

            // Whole-word: bytes outside the match must not be ASCII word chars. Any byte >= 128 is part
            // of a multi-byte UTF-8 sequence representing a non-ASCII char, which is never a word char
            // under our ASCII-word-char definition, so it counts as a boundary.
            if (wholeWord)
            {
                var leftOk = hitStart == 0 || !IsAsciiWordByte(content[hitStart - 1]);
                var rightOk = hitEnd == content.Length || !IsAsciiWordByte(content[hitEnd]);
                if (!(leftOk && rightOk))
                {
                    pos = hitStart + 1;
                    continue;
                }
            }

            // Advance the running line cursor up to the line containing this hit.
            while (lineStart < hitStart)
            {
                var nl = content[lineStart..hitStart].IndexOf((byte)'\n');
                if (nl < 0) break;
                lineStart += nl + 1;
                lineNumber++;
            }

            string lineText;
            if (cachedLineNumber == lineNumber && cachedLineText is not null)
            {
                lineText = cachedLineText;
            }
            else
            {
                // End of the line containing the hit.
                var afterMatch = hitEnd;
                var nlIndex = content[afterMatch..].IndexOf((byte)'\n');
                var lineEnd = nlIndex < 0 ? content.Length : afterMatch + nlIndex;
                if (lineEnd > lineStart && content[lineEnd - 1] == (byte)'\r') lineEnd--;

                lineText = encoding.GetString(content[lineStart..lineEnd]);
                cachedLineNumber = lineNumber;
                cachedLineText = lineText;
            }

            // The column reported to the UI is char-based, not byte-based. For the all-ASCII prefix case
            // (very common in code), char count equals byte count and we skip the decode. Otherwise we
            // count chars in the prefix slice once.
            var prefix = content[lineStart..hitStart];
            var charColumn = IsAllAscii(prefix) ? prefix.Length : encoding.GetCharCount(prefix);

            hits ??= new List<LineMatch>();
            // Length is reported as a CHAR count: the UI's MatchText extracts the highlight via
            // lineText.Substring(Column, Length), which is char-indexed. Using patternBytes.Length
            // here would overshoot for non-ASCII patterns (e.g. "Größe" — 5 chars / 7 UTF-8 bytes).
            hits.Add(new LineMatch(lineNumber, charColumn, patternCharCount, lineText));

            pos = hitEnd;
        }

        // Catch the line counter up to the end of the slice so chunked callers resume correctly.
        while (lineStart < content.Length)
        {
            var nl = content[lineStart..].IndexOf((byte)'\n');
            if (nl < 0) break;
            lineStart += nl + 1;
            lineNumber++;
        }
        return lineNumber;
    }

    private static int IndexOfAsciiCaseInsensitive(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needleLower)
    {
        // Pre-compute upper variants of pattern bytes that are ASCII letters (only those flip when folded).
        var first = needleLower[0];
        var firstUpper = (byte)(first is >= (byte)'a' and <= (byte)'z' ? first - 32 : first);

        var pos = 0;
        while (pos <= haystack.Length - needleLower.Length)
        {
            // SIMD-vectorized fast skip to the next byte that could begin the pattern.
            int skip;
            if (first == firstUpper)
                skip = haystack[pos..].IndexOf(first);
            else
                skip = haystack[pos..].IndexOfAny(first, firstUpper);
            if (skip < 0) return -1;

            var candidate = pos + skip;
            if (candidate + needleLower.Length > haystack.Length) return -1;

            // Verify the rest of the pattern matches case-insensitively.
            var ok = true;
            for (var i = 0; i < needleLower.Length; i++)
            {
                var hb = haystack[candidate + i];
                var hbLower = (byte)(hb is >= (byte)'A' and <= (byte)'Z' ? hb + 32 : hb);
                if (hbLower != needleLower[i]) { ok = false; break; }
            }
            if (ok) return candidate;

            pos = candidate + 1;
        }
        return -1;
    }

    private static bool IsAsciiWordByte(byte b) =>
        (uint)(b - (byte)'0') <= 9 ||
        (uint)((b | 0x20) - (byte)'a') <= 25 ||
        b == (byte)'_';

    private static bool IsAllAscii(ReadOnlySpan<byte> bytes) => Ascii.IsValid(bytes);

    /// <summary>
    /// Multi-line search: feeds the entire file content to the matcher in one call so regex
    /// patterns can match across newlines. Each emitted <see cref="LineMatch"/> is anchored to
    /// the line where the match starts; the highlight length is clamped to that line so the UI
    /// renders sensibly. Matches that span more lines still count as a single match.
    /// </summary>
    private static FileMatch? SearchWholeText(string path, string text, Encoding encoding, IMatcher matcher, CancellationToken ct)
    {
        var spans = new List<MatchSpan>();
        matcher.FindMatches(text, spans);
        if (spans.Count == 0)
            return null;

        var hits = new List<LineMatch>(spans.Count);
        var lineStart = 0;
        var lineNumber = 1;

        foreach (var span in spans)
        {
            ct.ThrowIfCancellationRequested();

            // Walk forward to the line containing the match start. Spans are returned in order,
            // so we never need to rewind.
            while (lineStart < span.Column)
            {
                var nl = text.IndexOf('\n', lineStart);
                if (nl < 0 || nl >= span.Column) break;
                lineStart = nl + 1;
                lineNumber++;
            }

            var lineEnd = text.IndexOf('\n', span.Column);
            if (lineEnd < 0) lineEnd = text.Length;
            var trimmedEnd = lineEnd;
            if (trimmedEnd > lineStart && text[trimmedEnd - 1] == '\r') trimmedEnd--;

            var lineText = text.Substring(lineStart, trimmedEnd - lineStart);
            var columnInLine = span.Column - lineStart;
            var highlightLength = Math.Max(0, Math.Min(span.Length, trimmedEnd - span.Column));
            hits.Add(new LineMatch(lineNumber, columnInLine, highlightLength, lineText));
        }
        return new FileMatch(path, encoding, hits, []);
    }

    private static int SearchDecoded(
        ReadOnlySpan<byte> content, Encoding encoding, IMatcher matcher,
        ref List<LineMatch>? hits, int startLineNumber, CancellationToken ct)
    {
        var text = encoding.GetString(content);
        var spans = new List<MatchSpan>(capacity: 8);
        var lineNumber = startLineNumber;
        var pos = 0;

        while (pos <= text.Length)
        {
            ct.ThrowIfCancellationRequested();

            var nl = text.IndexOf('\n', pos);
            var lineEnd = nl < 0 ? text.Length : nl;
            var trimmedEnd = lineEnd;
            if (trimmedEnd > pos && text[trimmedEnd - 1] == '\r')
                trimmedEnd--;
            var lineSpan = text.AsSpan(pos, trimmedEnd - pos);

            spans.Clear();
            matcher.FindMatches(lineSpan, spans);
            if (spans.Count > 0)
            {
                hits ??= new List<LineMatch>();
                var lineText = lineSpan.ToString();
                foreach (var span in spans)
                    hits.Add(new LineMatch(lineNumber, span.Column, span.Length, lineText));
            }

            if (nl < 0)
                break;
            pos = nl + 1;
            lineNumber++;
        }

        return lineNumber;
    }
}
