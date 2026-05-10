using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LionGrep.Models;

public sealed class Preset : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    private string? _hotkey;
    public string? Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }

    private bool _applyWhere;
    public bool ApplyWhere { get => _applyWhere; set => Set(ref _applyWhere, value); }
    private bool _applyWhat;
    public bool ApplyWhat { get => _applyWhat; set => Set(ref _applyWhat, value); }
    private bool _applyFilter;
    public bool ApplyFilter { get => _applyFilter; set => Set(ref _applyFilter, value); }

    // Where
    private string _searchIn = "";
    public string SearchIn { get => _searchIn; set => Set(ref _searchIn, value); }

    // What
    private string _searchPattern = "";
    public string SearchPattern { get => _searchPattern; set => Set(ref _searchPattern, value); }
    private string _replacePattern = "";
    public string ReplacePattern { get => _replacePattern; set => Set(ref _replacePattern, value); }
    private bool _useRegex;
    public bool UseRegex { get => _useRegex; set => Set(ref _useRegex, value); }
    private bool _caseSensitive;
    public bool CaseSensitive { get => _caseSensitive; set => Set(ref _caseSensitive, value); }
    private bool _wholeWord;
    public bool WholeWord { get => _wholeWord; set => Set(ref _wholeWord, value); }
    private bool _preserveCase = true;
    public bool PreserveCase { get => _preserveCase; set => Set(ref _preserveCase, value); }
    private bool _dotMatchesNewline;
    public bool DotMatchesNewline { get => _dotMatchesNewline; set => Set(ref _dotMatchesNewline, value); }
    private bool _searchInNames;
    public bool SearchInNames { get => _searchInNames; set => Set(ref _searchInNames, value); }
    private bool _keepFileDate;
    public bool KeepFileDate { get => _keepFileDate; set => Set(ref _keepFileDate, value); }

    // Filter
    private string _fileNames = "";
    public string FileNames { get => _fileNames; set => Set(ref _fileNames, value); }
    private bool _fileNamesRegex;
    public bool FileNamesRegex { get => _fileNamesRegex; set => Set(ref _fileNamesRegex, value); }
    private string _excludePaths = "";
    public string ExcludePaths { get => _excludePaths; set => Set(ref _excludePaths, value); }
    private bool _excludePathsRegex;
    public bool ExcludePathsRegex { get => _excludePathsRegex; set => Set(ref _excludePathsRegex, value); }
    private int _sizeModeIndex;
    public int SizeModeIndex { get => _sizeModeIndex; set => Set(ref _sizeModeIndex, value); }
    private double _sizeKb = 256;
    public double SizeKb { get => _sizeKb; set => Set(ref _sizeKb, value); }
    private double _sizeKbUpper = 1024;
    public double SizeKbUpper { get => _sizeKbUpper; set => Set(ref _sizeKbUpper, value); }
    private int _dateModeIndex;
    public int DateModeIndex { get => _dateModeIndex; set => Set(ref _dateModeIndex, value); }
    private DateTime? _dateFrom;
    public DateTime? DateFrom { get => _dateFrom; set => Set(ref _dateFrom, value); }
    private DateTime? _dateTo;
    public DateTime? DateTo { get => _dateTo; set => Set(ref _dateTo, value); }
    private bool _includeSubfolders = true;
    public bool IncludeSubfolders { get => _includeSubfolders; set => Set(ref _includeSubfolders, value); }
    private bool _includeSystem;
    public bool IncludeSystem { get => _includeSystem; set => Set(ref _includeSystem, value); }
    private bool _includeHidden;
    public bool IncludeHidden { get => _includeHidden; set => Set(ref _includeHidden, value); }
    private bool _followSymlinks;
    public bool FollowSymlinks { get => _followSymlinks; set => Set(ref _followSymlinks, value); }
    private bool _skipBinaryFiles;
    public bool SkipBinaryFiles { get => _skipBinaryFiles; set => Set(ref _skipBinaryFiles, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
