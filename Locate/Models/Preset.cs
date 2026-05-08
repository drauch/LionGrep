namespace Locate.Models;

public sealed class Preset
{
    public string Name { get; set; } = "";
    public string? Hotkey { get; set; }

    public bool ApplyWhere { get; set; }
    public bool ApplyWhat { get; set; }
    public bool ApplyFilter { get; set; }

    // Where
    public string SearchIn { get; set; } = "";

    // What
    public string SearchPattern { get; set; } = "";
    public string ReplacePattern { get; set; } = "";
    public bool UseRegex { get; set; }
    public bool CaseSensitive { get; set; }
    public bool WholeWord { get; set; }
    public bool PreserveCase { get; set; } = true;
    public bool DotMatchesNewline { get; set; }
    public bool SearchInNames { get; set; }
    public bool KeepFileDate { get; set; }

    // Filter
    public string FileNames { get; set; } = "";
    public bool FileNamesRegex { get; set; }
    public string ExcludePaths { get; set; } = "";
    public bool ExcludePathsRegex { get; set; }
    public int SizeModeIndex { get; set; }
    public double SizeKb { get; set; } = 256;
    public int DateModeIndex { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool IncludeSubfolders { get; set; } = true;
    public bool IncludeSystem { get; set; }
    public bool IncludeHidden { get; set; }
    public bool FollowSymlinks { get; set; }
    public bool SkipBinaryFiles { get; set; }
}
