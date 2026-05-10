namespace LionGrep.Core.Logic;

/// <summary>What semantic role a piece of text plays in the rendered results view.</summary>
public enum HighlightKind
{
    /// <summary>Plain text — no background.</summary>
    None,
    /// <summary>The engine matched here (yellow background).</summary>
    EngineMatch,
    /// <summary>The user's live "search in results" filter matched here (blue background).
    /// Filter wins over <see cref="EngineMatch"/> on overlap because it reflects the user's
    /// current focus; the engine-match rendering still distinguishes itself by other means
    /// (font weight) so neither is lost.</summary>
    FilterMatch,
}

public readonly record struct HighlightRange(int Start, int Length);

public readonly record struct HighlightSegment(string Text, HighlightKind Kind);

/// <summary>
/// Splits a string into non-overlapping segments tagged with <see cref="HighlightKind"/> so the
/// UI can render each chunk with its own background. Pure function, used for file names, paths,
/// and matched-line text alike.
///
/// Overlap policy: <see cref="HighlightKind.FilterMatch"/> beats <see cref="HighlightKind.EngineMatch"/>
/// beats <see cref="HighlightKind.None"/>. Filter occurrences are case-insensitive (Ordinal); engine
/// ranges are taken as authoritative — the caller has already located them.
/// </summary>
public static class HighlightSegmenter
{
    public static IReadOnlyList<HighlightSegment> Build(
        string text,
        IReadOnlyList<HighlightRange>? engineMatches,
        string? filterText)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var hasEngine = engineMatches is { Count: > 0 };
        var hasFilter = !string.IsNullOrEmpty(filterText);
        if (!hasEngine && !hasFilter)
            return [new HighlightSegment(text, HighlightKind.None)];

        // Per-character priority array. We let later writes override earlier ones — engine ranges
        // are written first, then filter occurrences on top, so filter automatically wins.
        var kinds = new HighlightKind[text.Length];

        if (hasEngine)
        {
            foreach (var range in engineMatches!)
            {
                var start = Math.Max(0, range.Start);
                var end = Math.Min(text.Length, range.Start + range.Length);
                for (var i = start; i < end; i++)
                    kinds[i] = HighlightKind.EngineMatch;
            }
        }

        if (hasFilter)
        {
            var needle = filterText!;
            var idx = 0;
            while (idx <= text.Length - needle.Length)
            {
                var found = text.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                var end = found + needle.Length;
                for (var i = found; i < end; i++)
                    kinds[i] = HighlightKind.FilterMatch;
                // Advance by 1 (not needle.Length) so overlapping occurrences (e.g. "aa" in "aaaa")
                // all get highlighted. The kind array is idempotent under repeated assignment.
                idx = found + 1;
            }
        }

        // Coalesce contiguous same-kind ranges into segments.
        var segments = new List<HighlightSegment>();
        var segStart = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || kinds[i] != kinds[segStart])
            {
                segments.Add(new HighlightSegment(text[segStart..i], kinds[segStart]));
                segStart = i;
            }
        }
        return segments;
    }
}
