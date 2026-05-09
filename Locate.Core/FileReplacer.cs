using System.Text;
using System.Text.RegularExpressions;

namespace Locate.Core;

public sealed class FileReplacer
{
    public const long InMemoryThresholdBytes = 4L * 1024 * 1024;

    public ReplaceResult Replace(string path, ReplacementContext context, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(context);

        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
            return new ReplaceResult(path, 0);
        if (info.Length > InMemoryThresholdBytes)
            throw new NotSupportedException(
                $"Replace currently supports files up to {InMemoryThresholdBytes / 1024 / 1024} MiB (path: {path}, size: {info.Length}). Streaming replace for larger files is a v1.1 feature.");

        var bytes = File.ReadAllBytes(path);
        var detected = EncodingDetection.Detect(bytes);
        var contentBytes = bytes.AsSpan(detected.BomLength);
        var text = detected.Encoding.GetString(contentBytes);

        string output;
        int count;
        if (context.Search.UseRegex && context.Search.DotMatchesNewline)
        {
            // Multi-line replace: run the regex over the whole text in one shot so dot can cross newlines.
            (output, count) = ReplaceWholeText(text, context, ct);
        }
        else
        {
            var replacer = LineReplacerFactory.Create(context.Search, context.Replacement, context.PreserveCase);
            (output, count) = ProcessText(text, replacer, ct);
        }

        if (count == 0)
            return new ReplaceResult(path, 0);

        string? backupPath = null;
        if (context.CreateBackup)
        {
            backupPath = path + ".bak";
            // Copy preserves the source's last-write time, which is exactly what Undo needs to restore the original mtime.
            File.Copy(path, backupPath, overwrite: true);
        }

        WriteAtomic(path, detected.Encoding, output, info, context.KeepFileDate);
        return new ReplaceResult(path, count, backupPath);
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

    private static void WriteAtomic(string path, Encoding encoding, string output, FileInfo originalInfo, bool keepFileDate)
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
            try { File.Delete(temp); } catch { }
            throw;
        }

        if (keepFileDate)
            File.SetLastWriteTimeUtc(path, originalMtime);
    }
}
