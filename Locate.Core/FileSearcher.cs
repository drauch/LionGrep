using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Locate.Core;

public sealed class FileSearcher
{
    private const int BinaryProbeBytes = 8192;

    public FileMatch? Search(string path, IMatcher matcher, CancellationToken ct = default, bool skipBinary = false)
        => Search(path, matcher, skipBinary, out _, ct);

    public FileMatch? Search(string path, IMatcher matcher, bool skipBinary, out bool wasBinarySkipped, CancellationToken ct = default)
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

                var charCount = encoding.GetCharCount(lineBytes);
                if (charCount > buffer.Length)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                    buffer = ArrayPool<char>.Shared.Rent(charCount);
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
