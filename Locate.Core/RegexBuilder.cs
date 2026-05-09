using System.Text.RegularExpressions;

namespace Locate.Core;

/// <summary>
/// Single source of truth for translating <see cref="SearchOptions"/> into a compiled <see cref="Regex"/>.
/// Used by RegexMatcher (search), RegexLineReplacer (per-line replace), and FileReplacer (whole-text replace),
/// so all three apply <c>WholeWord</c>, casing, and Singleline consistently.
/// </summary>
internal static class RegexBuilder
{
    public static Regex Build(SearchOptions options)
        => Build(options.Pattern, options.CaseSensitive, options.WholeWord, options.DotMatchesNewline);

    public static Regex Build(string pattern, bool caseSensitive, bool wholeWord, bool dotMatchesNewline)
    {
        if (wholeWord)
            pattern = $@"\b(?:{pattern})\b";

        var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (!caseSensitive) options |= RegexOptions.IgnoreCase;
        if (dotMatchesNewline) options |= RegexOptions.Singleline;

        return new Regex(pattern, options);
    }
}
