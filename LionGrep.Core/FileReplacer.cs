using System.Text;

namespace LionGrep.Core;

public sealed class FileReplacer
{
    /// <summary>Threshold above which the line-by-line replace switches to streaming. Files at or
    /// below this size are processed entirely in memory (faster small-file path that mirrors v1.0).</summary>
    public const long InMemoryThresholdBytes = 4L * 1024 * 1024;

    /// <summary>Hard cap for DotMatchesNewline (singleline) regex replace. Multiline regex replace
    /// fundamentally requires the whole file in one .NET string, so we can't stream it; this cap
    /// keeps memory predictable. Bumped from 4 MiB (v1.0) to 256 MiB now that the line-by-line path
    /// handles arbitrary sizes.</summary>
    public const long MultilineMemoryCapBytes = 256L * 1024 * 1024;

    /// <summary>Hard cap on a single line's char buffer in the streaming path. Lines longer than
    /// this trigger NotSupportedException — pathological inputs (e.g. a 1 GiB minified JS line)
    /// would otherwise force unbounded buffer growth.</summary>
    private const int MaxStreamingLineChars = 32 * 1024 * 1024;

#pragma warning disable S2325 // Instance method by API design — callers and tests rely on `new FileReplacer().Replace(...)`.
    public ReplaceResult Replace(string path, ReplacementContext context, CancellationToken ct = default)
#pragma warning restore S2325
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(context);

        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
            return new ReplaceResult(path, 0);

        if (context.Search.UseRegex && context.Search.DotMatchesNewline)
        {
            if (info.Length > MultilineMemoryCapBytes)
                throw new NotSupportedException(
                    $"DotMatchesNewline regex replace is capped at {MultilineMemoryCapBytes / 1024 / 1024} MiB " +
                    $"(path: {path}, size: {info.Length}). Singleline regex semantics require the whole file " +
                    "loaded into a single .NET string; the line-by-line path supports any size.");
            return ReplaceInMemoryMultiline(path, info, context, ct);
        }

        if (info.Length <= InMemoryThresholdBytes)
            return ReplaceInMemoryLineByLine(path, info, context, ct);

        return ReplaceStreamingLineByLine(path, info, context, ct);
    }

    /// <summary>v1.0 in-memory path for files below the streaming threshold. Read all → decode →
    /// process → encode → atomic write. Faster for typical small files since we don't pay any
    /// streaming overhead.</summary>
    private static ReplaceResult ReplaceInMemoryLineByLine(string path, FileInfo info, ReplacementContext context, CancellationToken ct)
    {
        var bytes = File.ReadAllBytes(path);
        var detected = EncodingDetection.Detect(bytes);
        var contentBytes = bytes.AsSpan(detected.BomLength);
        var text = detected.Encoding.GetString(contentBytes);

        var replacer = LineReplacerFactory.Create(context.Search, context.Replacement, context.PreserveCase);
        var (output, count) = ProcessText(text, replacer, ct);

        if (count == 0) return new ReplaceResult(path, 0);

        string? backupPath = null;
        if (context.CreateBackup)
        {
            backupPath = path + "." + context.BackupExtension;
            File.Copy(path, backupPath, overwrite: true);
        }
        WriteAtomicFromString(path, detected.Encoding, output, info, context.KeepFileDate);
        return new ReplaceResult(path, count, backupPath);
    }

    /// <summary>In-memory path for multi-line regex replace (DotMatchesNewline). Same shape as v1.0
    /// but with the file-size cap raised to <see cref="MultilineMemoryCapBytes"/>.</summary>
    private static ReplaceResult ReplaceInMemoryMultiline(string path, FileInfo info, ReplacementContext context, CancellationToken ct)
    {
        var bytes = File.ReadAllBytes(path);
        var detected = EncodingDetection.Detect(bytes);
        var contentBytes = bytes.AsSpan(detected.BomLength);
        var text = detected.Encoding.GetString(contentBytes);

        var (output, count) = ReplaceWholeText(text, context, ct);

        if (count == 0) return new ReplaceResult(path, 0);

        string? backupPath = null;
        if (context.CreateBackup)
        {
            backupPath = path + "." + context.BackupExtension;
            File.Copy(path, backupPath, overwrite: true);
        }
        WriteAtomicFromString(path, detected.Encoding, output, info, context.KeepFileDate);
        return new ReplaceResult(path, count, backupPath);
    }

    /// <summary>
    /// Streaming path for line-by-line replace on files above the in-memory threshold. Reads chars
    /// via StreamReader, walks the char buffer finding <c>\n</c>, preserves <c>\r</c> and <c>\n</c>
    /// terminators verbatim across buffer boundaries (via a one-line lookbehind in the buffer
    /// itself), runs <see cref="ILineReplacer.ReplaceLine"/> per line, and writes to a sibling temp
    /// file via StreamWriter. Atomic <see cref="File.Replace(string, string, string?)"/> at the end.
    /// Discards the temp if no replacements were made — the original file is left untouched and
    /// its mtime is preserved naturally.
    /// </summary>
    private static ReplaceResult ReplaceStreamingLineByLine(
        string path, FileInfo info, ReplacementContext context, CancellationToken ct)
    {
        // Tiny upfront read so we can write the matching BOM (or no BOM) to the temp before the
        // StreamReader starts decoding bulk content. We can't ask StreamReader to detect-and-strip
        // for us, because then StreamWriter wouldn't have the encoding state needed to round-trip.
        DetectedEncoding detected;
        using (var headStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4))
        {
            Span<byte> head = stackalloc byte[4];
            var headRead = headStream.Read(head);
            detected = EncodingDetection.Detect(head[..headRead]);
        }

        var dir = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var originalMtime = info.LastWriteTimeUtc;

        var replacer = LineReplacerFactory.Create(context.Search, context.Replacement, context.PreserveCase);
        long totalCount = 0;

        try
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16))
            using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1 << 16))
            {
                input.Seek(detected.BomLength, SeekOrigin.Begin);

                // detectEncodingFromByteOrderMarks: false — we already detected and seeked past the BOM.
                // StreamWriter's encoding writes its preamble to the output stream automatically when
                // first chars land, so the BOM round-trips iff the detected encoding has one.
                using var reader = new StreamReader(input, detected.Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1 << 16, leaveOpen: true);
                using var writer = new StreamWriter(output, detected.Encoding, bufferSize: 1 << 16, leaveOpen: true);

                StreamLineByLine(reader, writer, replacer, ref totalCount, ct);
            }

            if (totalCount == 0)
            {
                File.Delete(tempPath);
                return new ReplaceResult(path, 0);
            }

            string? backupPath = null;
            if (context.CreateBackup)
            {
                backupPath = path + "." + context.BackupExtension;
                File.Copy(path, backupPath, overwrite: true);
            }

            File.Replace(tempPath, path, destinationBackupFileName: null);

            if (context.KeepFileDate)
                File.SetLastWriteTimeUtc(path, originalMtime);

            return new ReplaceResult(path, checked((int)totalCount), backupPath);
        }
        catch
        {
            try { File.Delete(tempPath); } catch (IOException) { /* best-effort cleanup; the original file is still intact */ }
            throw;
        }
    }

    /// <summary>
    /// Core streaming loop. Reads chars in chunks, processes complete lines (<c>\n</c>-terminated
    /// or — for the very last line — unterminated), and emits them via <paramref name="writer"/>.
    /// When a line spans a read boundary, the partial tail is shifted to the start of the buffer
    /// and the next read appends after it; <c>\r\n</c> sequences therefore stay intact even when
    /// the <c>\r</c> falls in one read and the <c>\n</c> in the next.
    /// </summary>
    private static void StreamLineByLine(StreamReader reader, StreamWriter writer, ILineReplacer replacer, ref long totalCount, CancellationToken ct)
    {
        const int InitialBufSize = 1 << 16;  // 64 Ki chars
        var buf = new char[InitialBufSize];
        var sb = new StringBuilder(capacity: 4096);
        var leftover = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = reader.Read(buf, leftover, buf.Length - leftover);
            var total = leftover + read;
            var eof = read == 0;

            if (total == 0) break;

            // Will be set to >0 again only if the inner loop exits via the partial-line branch
            // below. Resetting here (rather than at the bottom) guarantees we don't accidentally
            // clobber a carried tail when the partial branch already set it.
            leftover = 0;

            var pos = 0;
            while (pos < total)
            {
                ct.ThrowIfCancellationRequested();

                var nl = Array.IndexOf(buf, '\n', pos, total - pos);
                int lineEnd;
                bool hasTerminator;

                if (nl >= 0)
                {
                    lineEnd = nl;
                    hasTerminator = true;
                }
                else if (eof)
                {
                    // Final unterminated line.
                    lineEnd = total;
                    hasTerminator = false;
                }
                else
                {
                    // Partial line: carry the tail to the front of the buffer and refill.
                    var remaining = total - pos;
                    if (remaining == buf.Length)
                    {
                        // Buffer is full and contains no '\n'. Grow it; bail if we hit the cap.
                        if (buf.Length >= MaxStreamingLineChars)
                            throw new NotSupportedException(
                                $"Encountered a single line longer than {MaxStreamingLineChars / 1024 / 1024} MiB " +
                                "in the streaming replace path. Pathological inputs aren't supported.");
                        Array.Resize(ref buf, Math.Min(buf.Length * 2, MaxStreamingLineChars));
                    }
                    if (pos > 0 && remaining > 0)
                        Array.Copy(buf, pos, buf, 0, remaining);
                    leftover = remaining;
                    pos = total;
                    continue;
                }

                // Detect a `\r` immediately before the `\n` and split it off so the replacer sees
                // the line content only, not the terminator. The `\r` is re-emitted verbatim below.
                var contentEnd = lineEnd;
                var hasCr = false;
                if (hasTerminator && contentEnd > pos && buf[contentEnd - 1] == '\r')
                {
                    contentEnd--;
                    hasCr = true;
                }

                sb.Clear();
                var line = new ReadOnlySpan<char>(buf, pos, contentEnd - pos);
                totalCount += replacer.ReplaceLine(line, sb);

                if (hasCr) sb.Append('\r');
                if (hasTerminator) sb.Append('\n');

                writer.Write(sb);

                pos = hasTerminator ? lineEnd + 1 : total;
            }

            if (eof) break;
        }
    }

    private static (string Output, int Count) ReplaceWholeText(string text, ReplacementContext ctx, CancellationToken ct)
    {
        var regex = RegexBuilder.Build(ctx.Search);
        var count = 0;
        var output = regex.Replace(text, match =>
        {
            ct.ThrowIfCancellationRequested();
            count++;
            var expanded = match.Result(ctx.Replacement);
            return ctx.PreserveCase ? CasePreserver.Apply(expanded, match.Value) : expanded;
        });
        return (output, count);
    }

    private static (string Output, int Count) ProcessText(string text, ILineReplacer replacer, CancellationToken ct)
    {
        var output = new StringBuilder(text.Length);
        var count = 0;
        var pos = 0;

        while (pos <= text.Length)
        {
            ct.ThrowIfCancellationRequested();

            var nl = text.IndexOf('\n', pos);
            var lineEnd = nl < 0 ? text.Length : nl;
            var contentEnd = lineEnd;
            var hasCr = false;
            if (contentEnd > pos && text[contentEnd - 1] == '\r')
            {
                contentEnd--;
                hasCr = true;
            }

            count += replacer.ReplaceLine(text.AsSpan(pos, contentEnd - pos), output);

            if (hasCr) output.Append('\r');
            if (nl >= 0) output.Append('\n');

            if (nl < 0) break;
            pos = nl + 1;
        }

        return (output.ToString(), count);
    }

    private static void WriteAtomicFromString(string path, Encoding encoding, string output, FileInfo originalInfo, bool keepFileDate)
    {
        var dir = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        var originalMtime = originalInfo.LastWriteTimeUtc;

        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var preamble = encoding.GetPreamble();
                if (preamble.Length > 0)
                    fs.Write(preamble);
                fs.Write(encoding.GetBytes(output));
            }

            File.Replace(temp, path, destinationBackupFileName: null);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { /* best-effort cleanup; the original file is still intact */ }
            throw;
        }

        if (keepFileDate)
            File.SetLastWriteTimeUtc(path, originalMtime);
    }
}
