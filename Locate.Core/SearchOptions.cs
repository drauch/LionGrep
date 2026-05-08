namespace Locate.Core;

public sealed record SearchOptions
{
    public required string Pattern { get; init; }
    public bool UseRegex { get; init; }
    public bool CaseSensitive { get; init; }
    public bool WholeWord { get; init; }
    public bool DotMatchesNewline { get; init; }
    public bool SearchInNames { get; init; }
    public bool SkipBinaryFiles { get; init; }
}
