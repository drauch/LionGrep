namespace Locate.Core;

public static class MatcherFactory
{
    public static IMatcher Create(SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseRegex
            ? new RegexMatcher(options.Pattern, options.CaseSensitive, options.WholeWord, options.DotMatchesNewline)
            : new LiteralMatcher(options.Pattern, options.CaseSensitive, options.WholeWord);
    }
}
