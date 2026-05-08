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
        Results.CollectionChanged += (_, _) => RecomputeReplaceEnabled();
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
    [NotifyCanExecuteChangedFor(nameof(CancelSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    [NotifyPropertyChangedFor(nameof(SearchButtonVisibility))]
    [NotifyPropertyChangedFor(nameof(CancelButtonVisibility))]
    private bool _isSearching;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    private bool _isReplacing;

    [ObservableProperty] private bool _isReplaceEnabled;

    partial void OnIsSearchingChanged(bool value) => RecomputeReplaceEnabled();
    partial void OnIsReplacingChanged(bool value) => RecomputeReplaceEnabled();
    private void RecomputeReplaceEnabled() =>
        IsReplaceEnabled = !IsSearching && !IsReplacing && Results.Count > 0;

    public Visibility SearchButtonVisibility => IsSearching ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CancelButtonVisibility => IsSearching ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<FileMatchViewModel> Results { get; } = [];
    public ObservableCollection<Preset> Presets { get; } = [];

    /// <summary>Raised when a search or replace operation starts so the host window can collapse the form row.</summary>
    public event EventHandler? OperationStarted;

    /// <summary>Raised when a search finishes (success or cancel) so the host window can apply default sort.</summary>
    public event EventHandler? SearchCompleted;

    // ---- Commands ----
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchPattern) && !SearchInNames)
        {
            SetSummary("Provide a search pattern.");
            return;
        }

        var roots = SearchIn
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();
        if (roots.Count == 0)
        {
            SetSummary("Provide at least one directory in 'Search in'.");
            return;
        }

        // When there's exactly one search root, display each file's directory relative to it.
        string? singleRoot = null;
        if (roots.Count == 1)
        {
            try { singleRoot = System.IO.Path.GetFullPath(roots[0]); }
            catch { singleRoot = null; }
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

        var request = BuildRequest(roots);
        // SyncProgress runs handler on caller thread (worker), so updating an Interlocked counter is essentially free
        // and avoids flooding the UI dispatcher with one item per file.
        var examinedProgress = new SyncProgress<int>(c => Interlocked.Exchange(ref _examinedCounter, c));
        var rejectedProgress = new SyncProgress<int>(c => Interlocked.Exchange(ref _rejectedCounter, c));

        StartSummaryTimer();

        try
        {
            await Task.Run(() =>
            {
                foreach (var match in _searcher.Search(request, examinedProgress, rejectedProgress, ct))
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
            SetSummary($"{MatchedFileCount:N0} files, {MatchCount:N0} matches, {RejectedCount:N0} skipped.");
            SaveRecents();
        }
        catch (OperationCanceledException)
        {
            StopSummaryTimer();
            DrainPendingResults();
            UpdateCountsFromAtomic();
            SetSummary($"Cancelled. {MatchedFileCount:N0} files, {MatchCount:N0} matches, {RejectedCount:N0} skipped.");
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

    private void UpdateRunningSummary() =>
        SetSummary($"{MatchedFileCount:N0} files, {MatchCount:N0} matches, {RejectedCount:N0} skipped (running)");

    private void SetSummary(string text)
    {
        ResultsSummary = text;
        WindowTitle = text == "No search run yet." ? "Locate" : "Locate — " + text;
    }

    private bool CanSearch() => !IsSearching && !IsReplacing;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelSearch() => _searchCts?.Cancel();
    private bool CanCancel() => IsSearching;

    [RelayCommand(CanExecute = nameof(CanReplace))]
    private async Task ReplaceAsync()
    {
        if (Results.Count == 0)
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
                KeepFileDate: KeepFileDate);

            var totalReplacements = 0;
            var filesChanged = 0;
            var paths = Results.Select(r => r.Path).ToList();
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
                        }
                    }
                    catch (NotSupportedException) { /* file too large; skip */ }
                    catch (IOException) { /* in use; skip */ }
                    catch (UnauthorizedAccessException) { /* skip */ }
                }
            });
            _recents.Add(RecentsKeyReplacePattern, ReplacePattern);
            SetSummary($"Replaced {totalReplacements:N0} matches in {filesChanged:N0} files.");
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

    private bool CanReplace() => !IsSearching && !IsReplacing && Results.Count > 0;

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
        DateModeIndex = DateModeIndex,
        DateFrom = DateFrom?.DateTime,
        DateTo = DateTo?.DateTime,
        IncludeSubfolders = IncludeSubfolders,
        IncludeSystem = IncludeSystem,
        IncludeHidden = IncludeHidden,
        FollowSymlinks = FollowSymlinks,
    };
}

