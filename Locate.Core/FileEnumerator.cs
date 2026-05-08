using System.IO.Enumeration;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Locate.Core;

public readonly record struct EnumeratedFile(string FullPath, string RootPath);

public sealed class FileEnumerator
{
    public IEnumerable<EnumeratedFile> Enumerate(IReadOnlyList<string> roots, FileEnumerationOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(options);

        var fileFilter = CompileFileNameFilter(options);
        var pathExclude = CompilePathExclusion(options);

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = ComputeAttributesToSkip(options),
            ReturnSpecialDirectories = false,
        };

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
                    if (pathExclude is not null && pathExclude(relative))
                        return false;
                    if (!fileFilter(relative))
                        return false;

                    if (options.Size is { } size && !PassesSize(entry.Length, size))
                        return false;

                    if (options.Date is { } date && !PassesDate(entry.LastWriteTimeUtc.DateTime, date))
                        return false;

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
        FileAttributes skip = 0;
        if (!options.IncludeHidden) skip |= FileAttributes.Hidden;
        if (!options.IncludeSystemFiles) skip |= FileAttributes.System;
        if (!options.FollowSymbolicLinks) skip |= FileAttributes.ReparsePoint;
        return skip;
    }

    private static bool PassesSize(long bytes, SizeFilter filter) => filter.Mode switch
    {
        SizeFilterMode.LessThan => bytes < filter.Bytes,
        SizeFilterMode.GreaterThan => bytes > filter.Bytes,
        _ => true,
    };

    private static bool PassesDate(DateTime mtimeUtc, DateFilter filter)
    {
        var fileDate = mtimeUtc.Date;
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
