namespace Locate.Core;

public sealed record FileEnumerationOptions
{
    public bool IncludeSubfolders { get; init; } = true;
    public bool IncludeSystemFiles { get; init; }
    public bool IncludeHidden { get; init; }
    public bool FollowSymbolicLinks { get; init; }

    public SizeFilter? Size { get; init; }
    public DateFilter? Date { get; init; }

    /// <summary>Raw user-entered file-name pattern. In Glob mode, "|"-separated globs with optional "!" prefix to exclude. In Regex mode, the entire string is one regex.</summary>
    public string? FileNamePatterns { get; init; }
    public PatternMode FileNamePatternMode { get; init; } = PatternMode.Glob;

    /// <summary>Raw user-entered exclude-paths pattern. In Glob mode, "|"-separated globs; matches both file paths and directory paths (relative, "/" separators). In Regex mode, the entire string is one regex applied to the relative path.</summary>
    public string? ExcludePathPatterns { get; init; }
    public PatternMode ExcludePathPatternMode { get; init; } = PatternMode.Glob;
}
