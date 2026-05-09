namespace Locate.Core.Logic;

/// <summary>
/// Removes redundant search roots from a list of directory paths. If <c>C:\Foo</c> and
/// <c>C:\Foo\Bar</c> both appear in the input, walking <c>C:\Foo</c> already covers
/// <c>C:\Foo\Bar</c>, so enumerating <c>C:\Foo\Bar</c> a second time is wasted work — and
/// the same files would appear twice in the results. The deduper drops the descendants
/// while preserving the user's input box (the UI keeps the original list visible; only the
/// list passed to the search engine is filtered).
/// </summary>
public static class PathPrefixDedup
{
    /// <summary>Returns the input with strict descendants and exact duplicates removed,
    /// preserving the order of the surviving entries (first occurrence wins).</summary>
    public static IReadOnlyList<string> Remove(IReadOnlyList<string> paths)
    {
        if (paths.Count <= 1) return paths;

        // Step 1: drop exact duplicates by normalized path.
        var seen = new HashSet<string>(PathComparer);
        var unique = new List<(string Original, string Normalized)>(paths.Count);
        foreach (var p in paths)
        {
            var norm = TryNormalizeForPrefix(p);
            if (norm is null) continue;                 // skip unresolvable paths (the search itself will report them)
            if (seen.Add(norm)) unique.Add((p, norm));
        }

        // Step 2: drop strict descendants. A path P is subsumed if some other normalized path Q
        // is a strict prefix of P (length-wise) AND P actually starts with Q. Because both have
        // a trailing separator, "C:\Foo\" is NOT a prefix of "C:\FooBar\" — exactly what we want.
        var kept = new List<string>(unique.Count);
        for (var i = 0; i < unique.Count; i++)
        {
            var (orig, norm) = unique[i];
            var subsumed = false;
            for (var j = 0; j < unique.Count; j++)
            {
                if (i == j) continue;
                var otherNorm = unique[j].Normalized;
                if (norm.Length > otherNorm.Length
                    && norm.StartsWith(otherNorm, PathComparison))
                {
                    subsumed = true;
                    break;
                }
            }
            if (!subsumed) kept.Add(orig);
        }
        return kept;
    }

    private static string? TryNormalizeForPrefix(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var full = Path.GetFullPath(raw.Trim());
            // Append a separator so the prefix test treats only directory boundaries as matches.
            if (!full.EndsWith(Path.DirectorySeparatorChar)
                && !full.EndsWith(Path.AltDirectorySeparatorChar))
            {
                full += Path.DirectorySeparatorChar;
            }
            return full;
        }
        catch
        {
            return null;
        }
    }

    // Path comparison on Windows is case-insensitive (NTFS / ReFS); on Linux it's case-sensitive.
    // OperatingSystem.IsWindows() picks the right one at startup.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
