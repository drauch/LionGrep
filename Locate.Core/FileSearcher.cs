using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Locate.Core;

public sealed class FileSearcher
{
    private const int BinaryProbeBytes = 8192;

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
        if (info.Length > int.MaxValue)
            throw new NotSupportedException($"Files larger than 2 GiB are not yet supported (path: {path}).");

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

                // Byte-level fast path for ASCII literals over UTF-8 content. ASCII bytes only ever appear
                // as themselves in UTF-8 (never as a continuation byte), so we can scan the raw bytes with
                // SIMD-vectorized IndexOf instead of decoding every line — a substantial win for the common
                // case (short ASCII patterns like identifiers, file extensions, URLs).
                if (detected.Encoding is UTF8Encoding
                    && matcher is LiteralMatcher lit
                    && lit.AsciiPatternBytes is { } asciiPattern)
                {
                    return SearchUtf8AsciiLiteral(path, content, detected.Encoding, asciiPattern, lit.CaseSensitive, lit.WholeWord, ct);
                }

                return detected.Encoding is UTF8Encoding
                    ? SearchUtf8(path, content, detected.Encoding, matcher, ct)
                    : SearchDecoded(path, content, detected.Encoding, matcher, ct);
            }
            finally
            {
                if (ptr is not null)
                    view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
    }

    private static bool IsLikelyBinary(ReadOnlySpan<byte> content, Encoding encoding)
    {
        // BOM-detected UTF-16/32 are always treated as text — they legitimately have NUL bytes from ASCII chars.
        if (encoding is not UTF8Encoding) return false;
        var probe = content[..Math.Min(content.Length, BinaryProbeBytes)];
        return probe.IndexOf((byte)0) >= 0;
    }

    private static FileMatch? SearchUtf8(string path, ReadOnlySpan<byte> content, Encoding encoding, IMatcher matcher, CancellationToken ct)
    {
        List<LineMatch>? hits = null;
        var spans = new List<MatchSpan>(capacity: 8);
        var buffer = ArrayPool<char>.Shared.Rent(4096);
        try
        {
            var lineNumber = 1;
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
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return hits is null ? null : new FileMatch(path, encoding, hits, []);
    }

    /// <summary>
    /// SIMD-driven byte scan for pure-ASCII literal patterns over UTF-8 content. Skips per-line
    /// decoding entirely; only matched lines are decoded for display. Honors case-insensitive ASCII
    /// folding and whole-word boundaries (word chars are themselves ASCII, so a non-ASCII byte before/after
    /// a match always counts as a non-word boundary).
    /// </summary>
    private static FileMatch? SearchUtf8AsciiLiteral(
        string path,
        ReadOnlySpan<byte> content,
        Encoding encoding,
        byte[] asciiPattern,
        bool caseSensitive,
        bool wholeWord,
        CancellationToken ct)
    {
        if (asciiPattern.Length == 0) return null;

        List<LineMatch>? hits = null;

        // Maintain (lineNumber, lineStart) incrementally so each match doesn't re-walk from byte 0.
        var lineStart = 0;
        var lineNumber = 1;

        var pos = 0;
        while (pos <= content.Length - asciiPattern.Length)
        {
            ct.ThrowIfCancellationRequested();

            var hitOffset = caseSensitive
                ? content[pos..].IndexOf(asciiPattern)
                : IndexOfAsciiCaseInsensitive(content[pos..], asciiPattern);
            if (hitOffset < 0) break;

            var hitStart = pos + hitOffset;
            var hitEnd = hitStart + asciiPattern.Length;

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

            // End of the line containing the hit.
            var afterMatch = hitEnd;
            var nlIndex = content[afterMatch..].IndexOf((byte)'\n');
            var lineEnd = nlIndex < 0 ? content.Length : afterMatch + nlIndex;
            if (lineEnd > lineStart && content[lineEnd - 1] == (byte)'\r') lineEnd--;

            var lineBytes = content[lineStart..lineEnd];
            var lineText = encoding.GetString(lineBytes);

            // The column reported to the UI is char-based, not byte-based. For the all-ASCII prefix case
            // (very common in code), char count equals byte count and we skip the decode. Otherwise we
            // count chars in the prefix slice once.
            var prefix = content[lineStart..hitStart];
            var charColumn = IsAllAscii(prefix) ? prefix.Length : encoding.GetCharCount(prefix);

            hits ??= new List<LineMatch>();
            hits.Add(new LineMatch(lineNumber, charColumn, asciiPattern.Length, lineText));

            pos = hitEnd;
        }

        return hits is null ? null : new FileMatch(path, encoding, hits, []);
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

    private static bool IsAllAscii(ReadOnlySpan<byte> bytes)
    {
        // Vectorized scan for any byte >= 128. Returns true if none.
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] >= 128) return false;
        return true;
    }

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

    private static FileMatch? SearchDecoded(string path, ReadOnlySpan<byte> content, Encoding encoding, IMatcher matcher, CancellationToken ct)
    {
        var text = encoding.GetString(content);
        List<LineMatch>? hits = null;
        var spans = new List<MatchSpan>(capacity: 8);
        var lineNumber = 1;
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

        return hits is null ? null : new FileMatch(path, encoding, hits, []);
    }
}
