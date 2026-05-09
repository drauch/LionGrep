using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Locate.Core;
using Locate.Models;
using Locate.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Locate.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public const string RecentsKeySearchIn = "SearchIn";
    public const string RecentsKeySearchPattern = "SearchPattern";
    public const string RecentsKeyReplacePattern = "ReplacePattern";
    public const string RecentsKeyFileNames = "FileNames";
    public const string RecentsKeyExcludePaths = "ExcludePaths";

    private readonly DispatcherQueue _dispatcher;
    private readonly Searcher _searcher = new();
    private readonly FileReplacer _fileReplacer = new();
    private readonly RecentsStore _recents;
    private readonly SettingsStore _settingsStore;
    private readonly PresetsStore _presetsStore;
    private CancellationTokenSource? _searchCts;

    private long _examinedCounter;
    private long _rejectedCounter;
    private long _matchedCounter;
    private long _matchCounter;
    private readonly ConcurrentQueue<FileMatchViewModel> _pendingResults = new();
    private Microsoft.UI.Xaml.DispatcherTimer? _summaryTimer;

    public MainViewModel(DispatcherQueue dispatcher, RecentsStore recents, SettingsStore settingsStore, PresetsStore presetsStore)
    {
        _dispatcher = dispatcher;
        _recents = recents;
        _settingsStore = settingsStore;
        _presetsStore = presetsStore;
        Results.CollectionChanged += OnResultsCollectionChanged;
        FilteredResults.CollectionChanged += (_, _) =>
        {
            // The filter shrinking the visible set should disable Replace / Search-in-found and update the status text.
            RecomputeReplaceEnabled();
            SearchInFoundFilesCommand.NotifyCanExecuteChanged();
            ReplaceCommand.NotifyCanExecuteChanged();
            ReplaceWithBackupsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(FilterStatusText));
        };
        ReloadPresets();
        var settings = _settingsStore.Load();
        if (settings.LastForm is { } last)
        {
            // LastForm captures every group, so apply with all-true regardless of stored flags.
            last.ApplyWhere = true;
            last.ApplyWhat = true;
            last.ApplyFilter = true;
            ApplyPreset(last);
        }
        UpdateWindowTitle();
    }

    public RecentsStore Recents => _recents;

    // ---- Form state ----
    [ObservableProperty] private string _searchIn = "";
    [ObservableProperty] private string _searchPattern = "";
    [ObservableProperty] private string _replacePattern = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDotMatchesNewlineEnabled))]
    private bool _useRegex;

    [ObservableProperty] private bool _caseSensitive;
    [ObservableProperty] private bool _wholeWord;
    [ObservableProperty] private bool _preserveCase = true;
    [ObservableProperty] private bool _dotMatchesNewline;
    [ObservableProperty] private bool _searchInNames;
    [ObservableProperty] private bool _keepFileDate;
    [ObservableProperty] private bool _skipBinaryFiles = true;

    [ObservableProperty] private string _fileNames = "";
    [ObservableProperty] private bool _fileNamesRegex;
    [ObservableProperty] private string _excludePaths = "";
    [ObservableProperty] private bool _excludePathsRegex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeValueVisibility))]
    [NotifyPropertyChangedFor(nameof(SizeUpperVisibility))]
    private int _sizeModeIndex = 1;  // Default to "Less than" so a size box is shown.

    [ObservableProperty] private double _sizeKb = 256;
    [ObservableProperty] private double _sizeKbUpper = 1024;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateFromVisibility))]
    [NotifyPropertyChangedFor(nameof(DateToVisibility))]
    private int _dateModeIndex;

    [ObservableProperty] private DateTimeOffset? _dateFrom;
    [ObservableProperty] private DateTimeOffset? _dateTo;

    [ObservableProperty] private bool _includeSubfolders = true;
    [ObservableProperty] private bool _includeSystem;
    [ObservableProperty] private bool _includeHidden;
    [ObservableProperty] private bool _followSymlinks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchInBoxHeight))]
    private bool _isSearchInExpanded;

    public double SearchInBoxHeight => IsSearchInExpanded ? 80 : 28;

    public bool IsDotMatchesNewlineEnabled => UseRegex;
    public Visibility SizeValueVisibility => SizeModeIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SizeUpperVisibility => SizeModeIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DateFromVisibility => DateModeIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DateToVisibility => DateModeIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

    // ---- Result column widths (resizable + responsive) ----
    [ObservableProperty] private GridLength _nameWidth = new(240);
    [ObservableProperty] private GridLength _sizeWidth = new(70);
    [ObservableProperty] private GridLength _matchesWidth = new(70);
    [ObservableProperty] private GridLength _pathWidth = new(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _extWidth = new(50);
    [ObservableProperty] private GridLength _encodingWidth = new(80);
    [ObservableProperty] private GridLength _dateWidth = new(160);

    // ---- Status / counts ----
    [ObservableProperty] private string _resultsSummary = "No search run yet.";
    [ObservableProperty] private string _windowTitle = "Locate";
    [ObservableProperty] private int _examinedCount;
    [ObservableProperty] private int _rejectedCount;
    [ObservableProperty] private int _matchedFileCount;
    [ObservableProperty] private int _matchCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(InverseSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchInFoundFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceWithBackupsCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoReplaceCommand))]
    [NotifyPropertyChangedFor(nameof(SearchButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(CancelButtonVisibility))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(InverseSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchInFoundFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceWithBackupsCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoReplaceCommand))]
    private bool _isReplacing;

    [ObservableProperty] private bool _isReplaceEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoReplaceCommand))]
    private bool _hasUndoableBackups;

    // ---- Validation ----
    // Each input drives an IsXxxValid bool. When false, the corresponding XxxBorderBrush flips
    // red and the form's IsFormValid becomes false, which gates Search and Replace can-execute.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchInBorderBrush))]
    [NotifyPropertyChangedFor(nameof(IsFormValid))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(InverseSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceWithBackupsCommand))]
    private bool _isSearchInValid = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchPatternBorderBrush))]
    [NotifyPropertyChangedFor(nameof(IsFormValid))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(InverseSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceWithBackupsCommand))]
    private bool _isSearchPatternValid = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileNamesBorderBrush))]
    [NotifyPropertyChangedFor(nameof(IsFormValid))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(InverseSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceWithBackupsCommand))]
    private bool _isFileNamesValid = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludePathsBorderBrush))]
    [NotifyPropertyChangedFor(nameof(IsFormValid))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(InverseSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceWithBackupsCommand))]
    private bool _isExcludePathsValid = true;

    public bool IsFormValid =>
        IsSearchInValid && IsSearchPatternValid && IsFileNamesValid && IsExcludePathsValid;

    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush InvalidInputBrush =
        new(Microsoft.UI.Colors.IndianRed);

    public Microsoft.UI.Xaml.Media.Brush? SearchInBorderBrush => IsSearchInValid ? null : InvalidInputBrush;
    public Microsoft.UI.Xaml.Media.Brush? SearchPatternBorderBrush => IsSearchPatternValid ? null : InvalidInputBrush;
    public Microsoft.UI.Xaml.Media.Brush? FileNamesBorderBrush => IsFileNamesValid ? null : InvalidInputBrush;
    public Microsoft.UI.Xaml.Media.Brush? ExcludePathsBorderBrush => IsExcludePathsValid ? null : InvalidInputBrush;

    private readonly List<(string Path, string BackupPath, DateTime BackupMtimeUtc)> _lastBackups = [];

    partial void OnIsSearchingChanged(bool value) => RecomputeReplaceEnabled();
    partial void OnIsReplacingChanged(bool value) => RecomputeReplaceEnabled();
    private void RecomputeReplaceEnabled() =>
        IsReplaceEnabled = !IsSearching && !IsReplacing && FilteredResults.Count > 0;

    public Visibility SearchButtonVisibility => IsSearching ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CancelButtonVisibility => IsSearching ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The complete result set as the search engine yielded it (sort order preserved).</summary>
    public ObservableCollection<FileMatchViewModel> Results { get; } = [];

    /// <summary>What the UI displays — equal to <see cref="Results"/> when <see cref="FilterText"/> is empty,
    /// or a filtered subset (file path or any matched line text contains the filter, case-insensitive).</summary>
    public ObservableCollection<FileMatchViewModel> FilteredResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltering))]
    [NotifyPropertyChangedFor(nameof(FilterStatusText))]
    private string _filterText = "";

    /// <summary>When true, the file path is also tested against the filter; otherwise only matched-line text is.</summary>
    [ObservableProperty] private bool _filterIncludesPath;

    /// <summary>Drives the collapsible filter row in the SEARCH RESULTS section.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultFilterPanelVisibility))]
    private bool _isResultFilterPanelOpen;

    public Visibility ResultFilterPanelVisibility => IsResultFilterPanelOpen ? Visibility.Visible : Visibility.Collapsed;

    public bool IsFiltering => !string.IsNullOrEmpty(FilterText);
    public string FilterStatusText => IsFiltering
        ? $"showing {FilteredResults.Count:N0} of {Results.Count:N0}"
        : "";

    private DispatcherTimer? _filterDebounceTimer;
    private bool _suppressFilteredMirror;

    partial void OnFilterTextChanged(string value)
    {
        // Debounce so we don't rebuild the filtered view on every keystroke.
        _filterDebounceTimer?.Stop();
        _filterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _filterDebounceTimer.Tick += (_, _) =>
        {
            _filterDebounceTimer.Stop();
            RebuildFilteredResults();
        };
        _filterDebounceTimer.Start();
    }

    private void OnResultsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Keep FilteredResults in sync incrementally so we don't rebuild N items on each Add during search drain.
        if (!_suppressFilteredMirror)
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add when e.NewItems is not null:
                    foreach (FileMatchViewModel item in e.NewItems)
                        if (PassesFilter(item)) FilteredResults.Add(item);
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                    foreach (FileMatchViewModel item in e.OldItems)
                        FilteredResults.Remove(item);
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    FilteredResults.Clear();
                    break;
            }
        }

        // FilterStatusText reads "showing {X} of {N}" — N is Results.Count, so the denominator can
        // change even when the filtered set doesn't (a Result.Add for a file that doesn't pass the
        // filter). Notify here. The CanExecute notifications for Replace / SearchInFound depend on
        // FilteredResults.Count and are fired by the FilteredResults.CollectionChanged handler — no
        // need to double-fire them on every Results change.
        OnPropertyChanged(nameof(FilterStatusText));
    }

    private bool PassesFilter(FileMatchViewModel f)
    {
        if (string.IsNullOrEmpty(FilterText)) return true;
        if (FilterIncludesPath && f.Path.Contains(FilterText, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var line in f.Lines)
            if (line.LineText.Contains(FilterText, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    partial void OnFilterIncludesPathChanged(bool value)
    {
        // The check semantic just changed — re-evaluate the visible set immediately, no debounce.
        if (IsFiltering) RebuildFilteredResults();
    }

    partial void OnIsResultFilterPanelOpenChanged(bool value)
    {
        // Closing the panel clears the filter — no hidden state.
        if (!value) FilterText = "";
    }

    private void RebuildFilteredResults()
    {
        FilteredResults.Clear();
        foreach (var r in Results)
            if (PassesFilter(r)) FilteredResults.Add(r);
        OnPropertyChanged(nameof(FilterStatusText));
    }

    /// <summary>Replaces <see cref="Results"/> in one logical batch (used by sort).
    /// FilteredResults is rebuilt once at the end to avoid N intermediate updates.</summary>
    public void ReplaceResults(IReadOnlyList<FileMatchViewModel> ordered)
    {
        _suppressFilteredMirror = true;
        try
        {
            Results.Clear();
            foreach (var r in ordered) Results.Add(r);
        }
        finally
        {
            _suppressFilteredMirror = false;
        }
        RebuildFilteredResults();
    }
    public ObservableCollection<Preset> Presets { get; } = [];

    /// <summary>Raised when a search or replace operation starts so the host window can collapse the form row.</summary>
    public event EventHandler? OperationStarted;

    /// <summary>Raised when a search finishes (success or cancel) so the host window can apply default sort.</summary>
    public event EventHandler? SearchCompleted;

    // ---- Commands ----
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private Task SearchAsync() => RunSearchAsync(invert: false, restrictToPaths: null);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private Task InverseSearchAsync() => RunSearchAsync(invert: true, restrictToPaths: null);

    [RelayCommand(CanExecute = nameof(CanSearchInFound))]
    private Task SearchInFoundFilesAsync()
    {
        // Snapshot the visible (filtered) paths before Results is cleared by the search,
        // so an active filter narrows the follow-up search exactly the way the user expects.
        var paths = FilteredResults.Select(r => r.Path).ToList();
        return RunSearchAsync(invert: false, restrictToPaths: paths);
    }
    private bool CanSearchInFound() => !IsSearching && !IsReplacing && FilteredResults.Count > 0;

    private async Task RunSearchAsync(bool invert, IReadOnlyList<string>? restrictToPaths)
    {
        var isFileList = restrictToPaths is not null;

        if (string.IsNullOrWhiteSpace(SearchPattern) && !SearchInNames)
        {
            SetSummary("Provide a search pattern.");
            return;
        }

        IReadOnlyList<string>? roots = null;
        string? singleRoot = null;
        if (!isFileList)
        {
            var rawRoots = SearchIn
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .ToList();
            // Drop redundant roots (descendants and exact dups) before passing to the engine. Saves
            // double-walking shared subtrees and prevents duplicate result rows. The Search-in
            // textbox keeps the user's literal input so re-edit / save-as-preset are unaffected.
            roots = Locate.Core.Logic.PathPrefixDedup.Remove(rawRoots);
            if (roots.Count == 0)
            {
                SetSummary("Provide at least one directory in 'Search in'.");
                return;
            }
            if (roots.Count == 1)
            {
                try { singleRoot = System.IO.Path.GetFullPath(roots[0]); }
                catch { singleRoot = null; }
            }
        }

        OperationStarted?.Invoke(this, EventArgs.Empty);
        IsSearching = true;
        Results.Clear();
        while (_pendingResults.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _examinedCounter, 0);
        Interlocked.Exchange(ref _rejectedCounter, 0);
        Interlocked.Exchange(ref _matchedCounter, 0);
        Interlocked.Exchange(ref _matchCounter, 0);
        ExaminedCount = 0;
        RejectedCount = 0;
        MatchedFileCount = 0;
        MatchCount = 0;
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        // SyncProgress runs handler on caller thread (worker), so updating an Interlocked counter is essentially free
        // and avoids flooding the UI dispatcher with one item per file.
        var examinedProgress = new SyncProgress<int>(c => Interlocked.Exchange(ref _examinedCounter, c));
        var rejectedProgress = new SyncProgress<int>(c => Interlocked.Exchange(ref _rejectedCounter, c));

        StartSummaryTimer();

        var searchOptions = BuildSearchOptions() with { Invert = invert };

        try
        {
            await Task.Run(() =>
            {
                IEnumerable<FileMatch> matches = isFileList
                    ? _searcher.SearchFiles(restrictToPaths!, searchOptions, examinedProgress, rejectedProgress, ct)
                    : _searcher.Search(new SearchRequest(roots!, BuildEnumerationOptions(), searchOptions), examinedProgress, rejectedProgress, ct);

                foreach (var match in matches)
                {
                    ct.ThrowIfCancellationRequested();
                    var insertion = (int)Interlocked.Increment(ref _matchedCounter) - 1;
                    Interlocked.Add(ref _matchCounter, match.ContentMatches.Count + match.NameMatches.Count);
                    string? displayDir = null;
                    if (singleRoot is not null)
                    {
                        try
                        {
                            var rel = System.IO.Path.GetRelativePath(singleRoot, match.Path);
                            displayDir = System.IO.Path.GetDirectoryName(rel) ?? "";
                        }
                        catch { displayDir = null; }
                    }
                    _pendingResults.Enqueue(new FileMatchViewModel(this, match, insertion, displayDir));
                }
            }, ct);
            StopSummaryTimer();
            DrainPendingResults();
            UpdateCountsFromAtomic();
            SetSummary(BuildSummary(running: false));
            SaveRecents();
        }
        catch (OperationCanceledException)
        {
            StopSummaryTimer();
            DrainPendingResults();
            UpdateCountsFromAtomic();
            SetSummary("Cancelled — " + BuildSummary(running: false));
        }
        catch (Exception ex)
        {
            StopSummaryTimer();
            DrainPendingResults();
            SetSummary($"Error: {ex.Message}");
        }
        finally
        {
            StopSummaryTimer();
            IsSearching = false;
            _searchCts = null;
            SearchCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StartSummaryTimer()
    {
        StopSummaryTimer();
        _summaryTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _summaryTimer.Tick += OnSummaryTimerTick;
        _summaryTimer.Start();
    }

    private void StopSummaryTimer()
    {
        if (_summaryTimer is null) return;
        _summaryTimer.Stop();
        _summaryTimer.Tick -= OnSummaryTimerTick;
        _summaryTimer = null;
    }

    private void OnSummaryTimerTick(object? sender, object e)
    {
        DrainPendingResults();
        UpdateCountsFromAtomic();
        if (IsSearching)
            UpdateRunningSummary();
    }

    private void DrainPendingResults()
    {
        while (_pendingResults.TryDequeue(out var vm))
            Results.Add(vm);
    }

    private void UpdateCountsFromAtomic()
    {
        ExaminedCount = (int)Interlocked.Read(ref _examinedCounter);
        RejectedCount = (int)Interlocked.Read(ref _rejectedCounter);
        MatchedFileCount = (int)Interlocked.Read(ref _matchedCounter);
        MatchCount = (int)Interlocked.Read(ref _matchCounter);
    }

    private void UpdateRunningSummary() => SetSummary(BuildSummary(running: true));

    /// <summary>
    /// Format: "224 matches in 65 files (76 files searched, 50662 skipped)".
    /// "files searched" is the post-filter examined count (what the searcher actually opened).
    /// </summary>
    private string BuildSummary(bool running)
    {
        var trail = running ? " (running)" : "";
        return $"{MatchCount:N0} matches in {MatchedFileCount:N0} files "
             + $"({ExaminedCount:N0} files searched, {RejectedCount:N0} skipped){trail}";
    }

    private void SetSummary(string text)
    {
        ResultsSummary = text;
    }

    partial void OnSearchInChanged(string value)
    {
        UpdateWindowTitle();
        ValidateSearchIn();
    }

    partial void OnSearchPatternChanged(string value) => ValidateSearchPattern();
    partial void OnUseRegexChanged(bool value) => ValidateSearchPattern();
    partial void OnFileNamesChanged(string value) => ValidateFileNames();
    partial void OnFileNamesRegexChanged(bool value) => ValidateFileNames();
    partial void OnExcludePathsChanged(string value) => ValidateExcludePaths();
    partial void OnExcludePathsRegexChanged(bool value) => ValidateExcludePaths();

    private void ValidateSearchIn()
    {
        // Empty Search-in is "no value entered yet" — not a validation error in this sense; the
        // search itself reports "provide at least one directory". Only flag once the user has
        // actually typed paths AND at least one of them isn't a real directory.
        var roots = SearchIn
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();
        if (roots.Count == 0) { IsSearchInValid = true; return; }

        foreach (var r in roots)
        {
            try
            {
                if (!System.IO.Directory.Exists(r)) { IsSearchInValid = false; return; }
            }
            catch { IsSearchInValid = false; return; }
        }
        IsSearchInValid = true;
    }

    private void ValidateSearchPattern()
    {
        // Plain-text patterns are always "valid" (any string parses). Regex patterns must compile.
        IsSearchPatternValid = !UseRegex || string.IsNullOrEmpty(SearchPattern) || IsValidRegex(SearchPattern);
    }

    private void ValidateFileNames()
    {
        IsFileNamesValid = !FileNamesRegex || string.IsNullOrEmpty(FileNames) || IsValidRegex(FileNames);
    }

    private void ValidateExcludePaths()
    {
        IsExcludePathsValid = !ExcludePathsRegex || string.IsNullOrEmpty(ExcludePaths) || IsValidRegex(ExcludePaths);
    }

    private static bool IsValidRegex(string pattern)
    {
        try { _ = new System.Text.RegularExpressions.Regex(pattern); return true; }
        catch { return false; }
    }

    private void UpdateWindowTitle()
    {
        var firstRoot = SearchIn?
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        WindowTitle = string.IsNullOrWhiteSpace(firstRoot) ? "Locate" : $"Locate — {firstRoot}";
    }

    private bool CanSearch() => !IsSearching && !IsReplacing && IsFormValid;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelSearch() => _searchCts?.Cancel();
    private bool CanCancel() => IsSearching;

    [RelayCommand(CanExecute = nameof(CanReplace))]
    private Task ReplaceAsync() => DoReplaceAsync(withBackups: false);

    [RelayCommand(CanExecute = nameof(CanReplace))]
    private Task ReplaceWithBackupsAsync() => DoReplaceAsync(withBackups: true);

    private async Task DoReplaceAsync(bool withBackups)
    {
        if (FilteredResults.Count == 0)
        {
            SetSummary("Run a search first.");
            return;
        }
        if (string.IsNullOrEmpty(SearchPattern))
        {
            SetSummary("Search pattern is empty.");
            return;
        }

        OperationStarted?.Invoke(this, EventArgs.Empty);
        IsReplacing = true;
        try
        {
            var ctx = new ReplacementContext(
                Search: BuildSearchOptions(),
                Replacement: ReplacePattern,
                PreserveCase: PreserveCase,
                KeepFileDate: KeepFileDate,
                CreateBackup: withBackups);

            var totalReplacements = 0;
            var filesChanged = 0;
            // Replace only the visible (filtered) set so an active filter narrows what gets rewritten.
            var paths = FilteredResults.Select(r => r.Path).ToList();
            var newBackups = new List<(string, string, DateTime)>();
            await Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    try
                    {
                        var result = _fileReplacer.Replace(path, ctx);
                        if (result.ReplacementCount > 0)
                        {
                            filesChanged++;
                            totalReplacements += result.ReplacementCount;
                            if (result.BackupPath is not null && File.Exists(result.BackupPath))
                                newBackups.Add((path, result.BackupPath, File.GetLastWriteTimeUtc(result.BackupPath)));
                        }
                    }
                    catch (NotSupportedException) { /* file too large; skip */ }
                    catch (IOException) { /* in use; skip */ }
                    catch (UnauthorizedAccessException) { /* skip */ }
                }
            });
            _recents.Add(RecentsKeyReplacePattern, ReplacePattern);

            if (withBackups)
            {
                // Each new backup-replace replaces the undo set, so a previous replace's .bak files become orphaned;
                // they remain on disk for safety but can no longer be undone via the UI.
                _lastBackups.Clear();
                _lastBackups.AddRange(newBackups);
                HasUndoableBackups = _lastBackups.Count > 0;
            }

            var suffix = withBackups
                ? $" (backed up {newBackups.Count:N0} file(s) to .bak)"
                : "";
            SetSummary($"Replaced {totalReplacements:N0} matches in {filesChanged:N0} files{suffix}.");
        }
        catch (Exception ex)
        {
            SetSummary($"Replace error: {ex.Message}");
        }
        finally
        {
            IsReplacing = false;
        }
    }

    private bool CanReplace() => !IsSearching && !IsReplacing && FilteredResults.Count > 0 && IsFormValid;

    [RelayCommand(CanExecute = nameof(CanUndoReplace))]
    private async Task UndoReplaceAsync()
    {
        if (_lastBackups.Count == 0)
        {
            SetSummary("Nothing to undo.");
            return;
        }

        var keepDate = KeepFileDate;
        var snapshot = _lastBackups.ToList();

        OperationStarted?.Invoke(this, EventArgs.Empty);
        IsReplacing = true;
        var restored = 0;
        var failed = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var (path, bak, bakMtime) in snapshot)
                {
                    try
                    {
                        if (!File.Exists(bak)) { failed++; continue; }
                        File.Copy(bak, path, overwrite: true);
                        // Honor "Keep file date when replacing": after restore, the file's mtime should match
                        // the original (= the backup's mtime) instead of "now".
                        if (keepDate)
                            File.SetLastWriteTimeUtc(path, bakMtime);
                        File.Delete(bak);
                        restored++;
                    }
                    catch { failed++; }
                }
            });
            _lastBackups.Clear();
            HasUndoableBackups = false;
            var failSuffix = failed > 0 ? $" ({failed:N0} failed)" : "";
            SetSummary($"Undo: restored {restored:N0} files from .bak{failSuffix}.");
        }
        catch (Exception ex)
        {
            SetSummary($"Undo error: {ex.Message}");
        }
        finally
        {
            IsReplacing = false;
        }
    }

    private bool CanUndoReplace() => HasUndoableBackups && !IsSearching && !IsReplacing;

    [RelayCommand]
    private void ToggleSearchInExpanded() => IsSearchInExpanded = !IsSearchInExpanded;

    [RelayCommand]
    private void ResetWhat()
    {
        SearchPattern = "";
        ReplacePattern = "";
        UseRegex = false;
        CaseSensitive = false;
        WholeWord = false;
        PreserveCase = true;
        DotMatchesNewline = false;
        SearchInNames = false;
        KeepFileDate = false;
    }

    [RelayCommand]
    private void ResetFilters()
    {
        FileNames = "";
        FileNamesRegex = false;
        ExcludePaths = "";
        ExcludePathsRegex = false;
        SizeModeIndex = 1;          // Less than (matches the initial app default)
        SizeKb = 256;
        SizeKbUpper = 1024;
        DateModeIndex = 0;
        DateFrom = null;
        DateTo = null;
        IncludeSubfolders = true;
        IncludeSystem = false;
        IncludeHidden = false;
        FollowSymlinks = false;
        SkipBinaryFiles = true;
    }

    private void SaveLastForm()
    {
        var snapshot = SnapshotAsPreset("__last__");
        snapshot.ApplyWhere = true;
        snapshot.ApplyWhat = true;
        snapshot.ApplyFilter = true;

        var settings = _settingsStore.Load();
        settings.LastForm = snapshot;
        _settingsStore.Save(settings);
    }

    /// <summary>Sets IsExpanded on every result, in dispatcher batches so the UI thread stays responsive on large lists.</summary>
    public async Task ExpandAllAsync(bool expand)
    {
        const int BatchSize = 50;
        for (var i = 0; i < Results.Count; i += BatchSize)
        {
            var end = Math.Min(i + BatchSize, Results.Count);
            for (var j = i; j < end; j++)
                Results[j].IsExpanded = expand;
            await Task.Yield();
        }
    }

    // ---- Helpers ----
    private SearchRequest BuildRequest(IReadOnlyList<string> roots)
    {
        return new SearchRequest(
            Roots: roots,
            Enumeration: BuildEnumerationOptions(),
            Search: BuildSearchOptions());
    }

    private SearchOptions BuildSearchOptions() => new()
    {
        Pattern = SearchPattern,
        UseRegex = UseRegex,
        CaseSensitive = CaseSensitive,
        WholeWord = WholeWord,
        DotMatchesNewline = UseRegex && DotMatchesNewline,
        SearchInNames = SearchInNames,
        SkipBinaryFiles = SkipBinaryFiles,
    };

    private FileEnumerationOptions BuildEnumerationOptions() => new()
    {
        IncludeSubfolders = IncludeSubfolders,
        IncludeSystemFiles = IncludeSystem,
        IncludeHidden = IncludeHidden,
        FollowSymbolicLinks = FollowSymlinks,
        FileNamePatterns = string.IsNullOrWhiteSpace(FileNames) ? null : FileNames,
        FileNamePatternMode = FileNamesRegex ? PatternMode.Regex : PatternMode.Glob,
        ExcludePathPatterns = string.IsNullOrWhiteSpace(ExcludePaths) ? null : ExcludePaths,
        ExcludePathPatternMode = ExcludePathsRegex ? PatternMode.Regex : PatternMode.Glob,
        Size = SizeModeIndex switch
        {
            1 => new SizeFilter(SizeFilterMode.LessThan, (long)(SizeKb * 1024)),
            2 => new SizeFilter(SizeFilterMode.GreaterThan, (long)(SizeKb * 1024)),
            3 => new SizeFilter(SizeFilterMode.Between, (long)(SizeKb * 1024), (long)(SizeKbUpper * 1024)),
            _ => null,
        },
        Date = (DateModeIndex, DateFrom, DateTo) switch
        {
            (1, { } from, _) => new DateFilter(DateFilterMode.NewerThan, from.DateTime),
            (2, { } from, _) => new DateFilter(DateFilterMode.OlderThan, from.DateTime),
            (3, { } from, _) => new DateFilter(DateFilterMode.ExactlyOn, from.DateTime),
            (4, { } from, { } to) => new DateFilter(DateFilterMode.Between, from.DateTime, to.DateTime),
            _ => null,
        },
    };

    private void SaveRecents()
    {
        _recents.Add(RecentsKeySearchIn, SearchIn);
        _recents.Add(RecentsKeySearchPattern, SearchPattern);
        _recents.Add(RecentsKeyFileNames, FileNames);
        _recents.Add(RecentsKeyExcludePaths, ExcludePaths);
        SaveLastForm();
    }

    // ---- Presets ----
    public void ReloadPresets()
    {
        Presets.Clear();
        foreach (var p in _presetsStore.Load())
            Presets.Add(p);
    }

    public void ApplyPreset(Preset preset)
    {
        if (preset.ApplyWhere)
        {
            SearchIn = preset.SearchIn;
        }
        if (preset.ApplyWhat)
        {
            SearchPattern = preset.SearchPattern;
            ReplacePattern = preset.ReplacePattern;
            UseRegex = preset.UseRegex;
            CaseSensitive = preset.CaseSensitive;
            WholeWord = preset.WholeWord;
            PreserveCase = preset.PreserveCase;
            DotMatchesNewline = preset.DotMatchesNewline;
            SearchInNames = preset.SearchInNames;
            KeepFileDate = preset.KeepFileDate;
            SkipBinaryFiles = preset.SkipBinaryFiles;
        }
        if (preset.ApplyFilter)
        {
            FileNames = preset.FileNames;
            FileNamesRegex = preset.FileNamesRegex;
            ExcludePaths = preset.ExcludePaths;
            ExcludePathsRegex = preset.ExcludePathsRegex;
            SizeModeIndex = preset.SizeModeIndex;
            SizeKb = preset.SizeKb;
            SizeKbUpper = preset.SizeKbUpper;
            DateModeIndex = preset.DateModeIndex;
            DateFrom = preset.DateFrom;
            DateTo = preset.DateTo;
            IncludeSubfolders = preset.IncludeSubfolders;
            IncludeSystem = preset.IncludeSystem;
            IncludeHidden = preset.IncludeHidden;
            FollowSymlinks = preset.FollowSymlinks;
        }
    }

    public Preset SnapshotAsPreset(string name) => new()
    {
        Name = name,
        ApplyWhere = !string.IsNullOrWhiteSpace(SearchIn),
        ApplyWhat = !string.IsNullOrEmpty(SearchPattern),
        ApplyFilter = !string.IsNullOrWhiteSpace(FileNames) || !string.IsNullOrWhiteSpace(ExcludePaths)
                      || SizeModeIndex > 0 || DateModeIndex > 0
                      || !IncludeSubfolders || IncludeSystem || IncludeHidden || FollowSymlinks,
        SearchIn = SearchIn,
        SearchPattern = SearchPattern,
        ReplacePattern = ReplacePattern,
        UseRegex = UseRegex,
        CaseSensitive = CaseSensitive,
        WholeWord = WholeWord,
        PreserveCase = PreserveCase,
        DotMatchesNewline = DotMatchesNewline,
        SearchInNames = SearchInNames,
        KeepFileDate = KeepFileDate,
        SkipBinaryFiles = SkipBinaryFiles,
        FileNames = FileNames,
        FileNamesRegex = FileNamesRegex,
        ExcludePaths = ExcludePaths,
        ExcludePathsRegex = ExcludePathsRegex,
        SizeModeIndex = SizeModeIndex,
        SizeKb = SizeKb,
        SizeKbUpper = SizeKbUpper,
        DateModeIndex = DateModeIndex,
        DateFrom = DateFrom?.DateTime,
        DateTo = DateTo?.DateTime,
        IncludeSubfolders = IncludeSubfolders,
        IncludeSystem = IncludeSystem,
        IncludeHidden = IncludeHidden,
        FollowSymlinks = FollowSymlinks,
    };
}

