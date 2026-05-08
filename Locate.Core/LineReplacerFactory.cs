namespace Locate.Core;

internal static class LineReplacerFactory
{
    public static ILineReplacer Create(SearchOptions search, string replacement, bool preserveCase)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(replacement);

        return search.UseRegex
            ? new RegexLineReplacer(
                search.Pattern,
                search.CaseSensitive,
                search.WholeWord,
                search.DotMatchesNewline,
                replacement,
                preserveCase)
            : new LiteralLineReplacer(
                search.Pattern,
                search.CaseSensitive,
                search.WholeWord,
                replacement,
                preserveCase);
    }
}
