using System.IO.Enumeration;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace LionGrep.Core;

public readonly record struct EnumeratedFile(string FullPath, string RootPath);

public sealed class FileEnumerator
{
    public IEnumerable<EnumeratedFile> Enumerate(IReadOnlyList<string> roots, FileEnumerationOptions options, CancellationToken ct = default)
    {
        return Enumerate(roots, options, onFileRejected: null, ct);
    }

#pragma warning disable S2325 // Instance method by API design — every caller goes through `_enumerator.Enumerate(...)`.
    public IEnumerable<EnumeratedFile> Enumerate(IReadOnlyList<string> roots, FileEnumerationOptions options, IProgress<int>? onFileRejected, CancellationToken ct = default)
#pragma warning restore S2325
    {
        // Argument validation runs eagerly at call time; the iterator body lives in EnumerateCore
        // so callers don't have to start consuming the enumerable to learn they passed null.
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(options);
        return EnumerateCore(roots, options, onFileRejected, ct);
    }

    private static IEnumerable<EnumeratedFile> EnumerateCore(IReadOnlyList<string> roots, FileEnumerationOptions options, IProgress<int>? onFileRejected, CancellationToken ct)
    {
        var fileFilter = CompileFileNameFilter(options);
        var pathExclude = CompilePathExclusion(options);

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = ComputeAttributesToSkip(options),
            ReturnSpecialDirectories = false,
        };

        var rejected = 0;

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                continue;

            var rootFull = Path.GetFullPath(root);

            var enumerable = new FileSystemEnumerable<string>(
                rootFull,
                (ref FileSystemEntry entry) => entry.ToFullPath(),
                enumerationOptions)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                {
                    if (entry.IsDirectory)
                        return false;

                    var relative = ToRelative(rootFull, entry.ToFullPath());
                    if ((pathExclude is not null && pathExclude(relative))
                        || !fileFilter(relative)
                        || (options.Size is { } size && !PassesSize(entry.Length, size))
                        || (options.Date is { } date && !PassesDate(entry.LastWriteTimeUtc.DateTime, date)))
                    {
                        rejected++;
                        onFileRejected?.Report(rejected);
                        return false;
                    }
                    return true;
                },
                ShouldRecursePredicate = pathExclude is null
                    ? null
                    : (ref FileSystemEntry entry) =>
                    {
                        var relative = ToRelative(rootFull, entry.ToFullPath());
                        return !pathExclude(relative);
                    },
            };

            foreach (var path in enumerable)
            {
                ct.ThrowIfCancellationRequested();
                yield return new EnumeratedFile(path, rootFull);
            }
        }
    }

    private static string ToRelative(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        return relative.Replace('\\', '/');
    }

    private static FileAttributes ComputeAttributesToSkip(FileEnumerationOptions options)
    {
        FileAttributes skip = FileAttributes.None;
        if (!options.IncludeHidden) skip |= FileAttributes.Hidden;
        if (!options.IncludeSystemFiles) skip |= FileAttributes.System;
        if (!options.FollowSymbolicLinks) skip |= FileAttributes.ReparsePoint;
        return skip;
    }

    private static bool PassesSize(long bytes, SizeFilter filter) => filter.Mode switch
    {
        SizeFilterMode.LessThan => bytes < filter.Bytes,
        SizeFilterMode.GreaterThan => bytes > filter.Bytes,
        SizeFilterMode.Between when filter.UpperBytes is { } upper => bytes >= filter.Bytes && bytes <= upper,
        _ => true,
    };

    private static bool PassesDate(DateTime mtimeUtc, DateFilter filter)
    {
        // Filter dates are user-thought *calendar dates* — the WinUI CalendarDatePicker emits a
        // DateTimeOffset, the VM unwraps it via .DateTime (which strips the offset and yields a
        // DateTime with Kind = Unspecified, numerically equal to the local-time midnight the user
        // picked). The file mtime here arrives as UTC, so we have to convert it to local before
        // taking .Date — otherwise a file written at 11 PM local in UTC+2 has a UTC date of 9 PM
        // the same day, but a file written at 1 AM local has a UTC date of "yesterday", which would
        // mis-classify it against a filter the user thought of in local-calendar terms.
        var fileDate = mtimeUtc.ToLocalTime().Date;
        var from = filter.From.Date;
        return filter.Mode switch
        {
            DateFilterMode.NewerThan => fileDate > from,
            DateFilterMode.OlderThan => fileDate < from,
            DateFilterMode.ExactlyOn => fileDate == from,
            DateFilterMode.Between when filter.To is { } to => fileDate >= from && fileDate <= to.Date,
            _ => false,
        };
    }

    private static Func<string, bool> CompileFileNameFilter(FileEnumerationOptions options)
    {
        var raw = options.FileNamePatterns;
        if (string.IsNullOrWhiteSpace(raw))
            return static _ => true;

        if (options.FileNamePatternMode == PatternMode.Regex)
        {
            var rx = new Regex(raw, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            return path => rx.IsMatch(path);
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        var hasInclude = false;
        foreach (var token in raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith('!'))
            {
                var pattern = NormalizeGlobToken(token[1..]);
                if (pattern.Length > 0)
                    matcher.AddExclude(pattern);
            }
            else
            {
                var pattern = NormalizeGlobToken(token);
                if (pattern.Length > 0)
                {
                    matcher.AddInclude(pattern);
                    hasInclude = true;
                }
            }
        }
        if (!hasInclude)
            matcher.AddInclude("**/*");

        return path => matcher.Match(path).HasMatches;
    }

    private static string NormalizeGlobToken(string token)
    {
        token = token.Trim();
        if (token.Length == 0)
            return token;
        if (token.Contains('/') || token.StartsWith("**", StringComparison.Ordinal))
            return token;
        return "**/" + token;
    }

    private static Func<string, bool>? CompilePathExclusion(FileEnumerationOptions options)
    {
        var raw = options.ExcludePathPatterns;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (options.ExcludePathPatternMode == PatternMode.Regex)
        {
            var rx = new Regex(raw, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            return relativePath => rx.IsMatch(relativePath);
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        var added = false;
        foreach (var token in raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pattern = NormalizeGlobToken(token);
            if (pattern.Length > 0)
            {
                matcher.AddInclude(pattern);
                added = true;
            }
        }
        if (!added) return null;

        return relativePath => matcher.Match(relativePath).HasMatches;
    }
}
