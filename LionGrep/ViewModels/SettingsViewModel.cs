using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LionGrep.Core.Logic;
using LionGrep.Models;
using LionGrep.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LionGrep.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;
    private readonly PresetsStore _presetsStore;
    private readonly Preset? _addTemplate;

    /// <param name="addTemplate">A snapshot of the main window's current form to use as the
    /// starting point when the user clicks Add. Pass <c>null</c> when no main form is available
    /// (Settings opened from a context where capturing it isn't meaningful).</param>
    public SettingsViewModel(SettingsStore settingsStore, PresetsStore presetsStore, Preset? addTemplate = null)
    {
        _settingsStore = settingsStore;
        _presetsStore = presetsStore;
        _addTemplate = addTemplate;

        var settings = _settingsStore.Load();
        _editorCommand = settings.EditorCommand;
        _dontWarnWhenReplacing = settings.DontWarnWhenReplacing;
        _rememberRecentValues = settings.RememberRecentValues;

        foreach (var p in _presetsStore.Load())
            Presets.Add(p);

        // Add/remove of any preset can introduce or resolve a name/hotkey duplicate against the
        // currently-selected preset, so re-emit validation on every collection change.
        Presets.CollectionChanged += (_, _) => RaiseSelectedPresetValidation();
    }

    // String initializer kept to satisfy non-nullable analysis even though the ctor overwrites it.
#pragma warning disable S3604 // Initializers ensure non-null defaults if a future ctor branch fails to seed.
    [ObservableProperty] private string _editorCommand = "";
    [ObservableProperty] private bool _dontWarnWhenReplacing;
    [ObservableProperty] private bool _rememberRecentValues = true;
#pragma warning restore S3604

    public ObservableCollection<Preset> Presets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPreset))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetNameError))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetNameErrorVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetNameBorderBrush))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetHotkeyError))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetHotkeyErrorVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetHotkeyBorderBrush))]
    [NotifyPropertyChangedFor(nameof(PresetSizeValueVisibility))]
    [NotifyPropertyChangedFor(nameof(PresetSizeUpperVisibility))]
    [NotifyPropertyChangedFor(nameof(PresetDateFromVisibility))]
    [NotifyPropertyChangedFor(nameof(PresetDateToVisibility))]
    [NotifyPropertyChangedFor(nameof(PresetSearchPatternBorderBrush))]
    [NotifyPropertyChangedFor(nameof(PresetFileNamesBorderBrush))]
    [NotifyPropertyChangedFor(nameof(PresetExcludePathsBorderBrush))]
    [NotifyCanExecuteChangedFor(nameof(RemovePresetCommand))]
    private Preset? _selectedPreset;

    public bool HasSelectedPreset => SelectedPreset is not null;

    // ---- Size / Date control visibility (mirrors MainViewModel.Size/DateXxxVisibility) ----
    //
    // Size:  0 = All sizes (no boxes), 1/2 = Less/Greater than (lower box only), 3 = Between (both).
    // Date:  0 = All dates  (no pickers), 1/2/3 = Newer/Older/Exactly (from only), 4 = Between (both).

    public Visibility PresetSizeValueVisibility => SelectedPreset?.SizeModeIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PresetSizeUpperVisibility => SelectedPreset?.SizeModeIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PresetDateFromVisibility => SelectedPreset?.DateModeIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PresetDateToVisibility => SelectedPreset?.DateModeIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

    // ---- Regex validation (mirrors MainViewModel's red-border treatment) ----
    //
    // Each regex-toggle pair (SearchPattern + UseRegex, FileNames + FileNamesRegex,
    // ExcludePaths + ExcludePathsRegex) flips red when the regex is on AND the pattern doesn't
    // compile. Pure-text patterns are never flagged — empty too is fine.

    public Brush? PresetSearchPatternBorderBrush => IsInvalidRegex(SelectedPreset?.SearchPattern, SelectedPreset?.UseRegex) ? InvalidInputBrush : null;
    public Brush? PresetFileNamesBorderBrush => IsInvalidRegex(SelectedPreset?.FileNames, SelectedPreset?.FileNamesRegex) ? InvalidInputBrush : null;
    public Brush? PresetExcludePathsBorderBrush => IsInvalidRegex(SelectedPreset?.ExcludePaths, SelectedPreset?.ExcludePathsRegex) ? InvalidInputBrush : null;

    private static bool IsInvalidRegex(string? pattern, bool? useRegex)
    {
        if (useRegex != true || string.IsNullOrEmpty(pattern)) return false;
        try
        {
            _ = new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            return false;
        }
        catch (System.ArgumentException) { return true; }
    }

    private static readonly SolidColorBrush InvalidInputBrush = new(Microsoft.UI.Colors.IndianRed);

    // ---- Name validation ----

    /// <summary>Error message for the SelectedPreset's Name field, or null when it's valid.
    /// Empty names are flagged (presets need a name to be referenced) and case-insensitive
    /// duplicates are flagged (so the user can tell two presets apart in the menu).</summary>
    public string? SelectedPresetNameError
    {
        get
        {
            if (SelectedPreset is null) return null;
            var name = SelectedPreset.Name;
            if (string.IsNullOrWhiteSpace(name)) return "Name is required.";
            if (HasDuplicateName(SelectedPreset)) return "Already used by another preset.";
            return null;
        }
    }

    public Visibility SelectedPresetNameErrorVisibility =>
        SelectedPresetNameError is not null ? Visibility.Visible : Visibility.Collapsed;

    public Brush? SelectedPresetNameBorderBrush =>
        SelectedPresetNameError is not null ? InvalidInputBrush : null;

    private bool HasDuplicateName(Preset selected)
    {
        var name = selected.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return false;   // empty is "required", not "duplicate"
        foreach (var p in Presets)
        {
            if (ReferenceEquals(p, selected)) continue;
            if (string.Equals(p.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ---- Hotkey validation ----

    /// <summary>Error message for the SelectedPreset's Hotkey field, or null if it's valid (or empty).
    /// Empty hotkeys are allowed — presets without a hotkey just lose the keyboard activation.
    /// Non-empty hotkeys must parse, must not collide with the built-in Search / Replace bindings,
    /// and must be unique across presets so each combo invokes exactly one.</summary>
    public string? SelectedPresetHotkeyError
    {
        get
        {
            if (SelectedPreset is null) return null;
            var hk = SelectedPreset.Hotkey;
            if (string.IsNullOrWhiteSpace(hk)) return null;
            if (HotkeyParser.IsReserved(hk)) return "Reserved by LionGrep (Search / Replace).";
            if (!HotkeyParser.TryParse(hk, out _)) return "Not a valid hotkey (e.g. Ctrl+1, Alt+F2).";
            if (HasDuplicateHotkey(SelectedPreset)) return "Already used by another preset.";
            return null;
        }
    }

    public Visibility SelectedPresetHotkeyErrorVisibility =>
        SelectedPresetHotkeyError is not null ? Visibility.Visible : Visibility.Collapsed;

    public Brush? SelectedPresetHotkeyBorderBrush =>
        SelectedPresetHotkeyError is not null ? InvalidInputBrush : null;

    private bool HasDuplicateHotkey(Preset selected)
    {
        if (string.IsNullOrWhiteSpace(selected.Hotkey)) return false;
        if (!HotkeyParser.TryParse(selected.Hotkey, out var mine)) return false;
        foreach (var p in Presets)
        {
            if (ReferenceEquals(p, selected)) continue;
            if (string.IsNullOrWhiteSpace(p.Hotkey)) continue;
            if (HotkeyParser.TryParse(p.Hotkey, out var other) && other == mine)
                return true;
        }
        return false;
    }

    partial void OnSelectedPresetChanging(Preset? value)
    {
        if (SelectedPreset is not null)
            SelectedPreset.PropertyChanged -= OnSelectedPresetPropertyChanged;
    }

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (value is not null)
            value.PropertyChanged += OnSelectedPresetPropertyChanged;
    }

    private void OnSelectedPresetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Preset.Name):
                OnPropertyChanged(nameof(SelectedPresetNameError));
                OnPropertyChanged(nameof(SelectedPresetNameErrorVisibility));
                OnPropertyChanged(nameof(SelectedPresetNameBorderBrush));
                break;
            case nameof(Preset.Hotkey):
                OnPropertyChanged(nameof(SelectedPresetHotkeyError));
                OnPropertyChanged(nameof(SelectedPresetHotkeyErrorVisibility));
                OnPropertyChanged(nameof(SelectedPresetHotkeyBorderBrush));
                break;
            case nameof(Preset.SizeModeIndex):
                OnPropertyChanged(nameof(PresetSizeValueVisibility));
                OnPropertyChanged(nameof(PresetSizeUpperVisibility));
                break;
            case nameof(Preset.DateModeIndex):
                OnPropertyChanged(nameof(PresetDateFromVisibility));
                OnPropertyChanged(nameof(PresetDateToVisibility));
                break;
            case nameof(Preset.SearchPattern):
            case nameof(Preset.UseRegex):
                OnPropertyChanged(nameof(PresetSearchPatternBorderBrush));
                break;
            case nameof(Preset.FileNames):
            case nameof(Preset.FileNamesRegex):
                OnPropertyChanged(nameof(PresetFileNamesBorderBrush));
                break;
            case nameof(Preset.ExcludePaths):
            case nameof(Preset.ExcludePathsRegex):
                OnPropertyChanged(nameof(PresetExcludePathsBorderBrush));
                break;
        }
    }

    private void RaiseSelectedPresetValidation()
    {
        OnPropertyChanged(nameof(SelectedPresetNameError));
        OnPropertyChanged(nameof(SelectedPresetNameErrorVisibility));
        OnPropertyChanged(nameof(SelectedPresetNameBorderBrush));
        OnPropertyChanged(nameof(SelectedPresetHotkeyError));
        OnPropertyChanged(nameof(SelectedPresetHotkeyErrorVisibility));
        OnPropertyChanged(nameof(SelectedPresetHotkeyBorderBrush));
    }

    [RelayCommand]
    private void AddPreset()
    {
        // No SavePresets here on purpose — Settings now follows OK/Cancel semantics: nothing
        // touches the registry until the user clicks Save. Cancel just closes and the in-memory
        // Add/Remove/Edit work is dropped on the floor.
        var preset = _addTemplate is null
            ? new Preset { Name = "New preset", ApplyWhere = true, ApplyWhat = true, ApplyFilter = true }
            : CloneAsNewPreset(_addTemplate);
        Presets.Add(preset);
        SelectedPreset = preset;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
    private void RemovePreset()
    {
        if (SelectedPreset is null) return;
        Presets.Remove(SelectedPreset);
        SelectedPreset = null;
        // No SavePresets — see AddPreset comment.
    }

    /// <summary>Clones the captured snapshot into a brand-new preset, with a sensible default
    /// name and all three Apply checkboxes ticked so the preset reproduces the current form
    /// when activated. Hotkey starts empty — uniqueness validation would otherwise duplicate
    /// whatever the snapshot happened to carry.</summary>
    private static Preset CloneAsNewPreset(Preset src) => new()
    {
        Name = "New preset",
        Hotkey = null,
        ApplyWhere = true,
        ApplyWhat = true,
        ApplyFilter = true,
        SearchIn = src.SearchIn,
        SearchPattern = src.SearchPattern,
        ReplacePattern = src.ReplacePattern,
        UseRegex = src.UseRegex,
        CaseSensitive = src.CaseSensitive,
        WholeWord = src.WholeWord,
        PreserveCase = src.PreserveCase,
        DotMatchesNewline = src.DotMatchesNewline,
        SearchInNames = src.SearchInNames,
        KeepFileDate = src.KeepFileDate,
        SkipBinaryFiles = src.SkipBinaryFiles,
        FileNames = src.FileNames,
        FileNamesRegex = src.FileNamesRegex,
        ExcludePaths = src.ExcludePaths,
        ExcludePathsRegex = src.ExcludePathsRegex,
        SizeModeIndex = src.SizeModeIndex,
        SizeKb = src.SizeKb,
        SizeKbUpper = src.SizeKbUpper,
        DateModeIndex = src.DateModeIndex,
        DateFrom = src.DateFrom,
        DateTo = src.DateTo,
        IncludeSubfolders = src.IncludeSubfolders,
        IncludeSystem = src.IncludeSystem,
        IncludeHidden = src.IncludeHidden,
        FollowSymlinks = src.FollowSymlinks,
    };

    public void SaveAll()
    {
        // Preserve the LastForm blob the main window writes — Save() only persists fields it knows about,
        // so re-load to keep that one (it's set by the main window on each search/replace).
        var existing = _settingsStore.Load();
        _settingsStore.Save(new AppSettings
        {
            EditorCommand = EditorCommand,
            DontWarnWhenReplacing = DontWarnWhenReplacing,
            RememberRecentValues = RememberRecentValues,
            LastForm = existing.LastForm,
        });
        // If the user just turned off "remember recents", purge any stored history.
        if (!RememberRecentValues)
            RecentsStore.ClearAll();
        SavePresets();
    }

    public void SavePresets() => _presetsStore.Save([.. Presets]);

    public void ResetEverything()
    {
        // Wipe the entire HKCU\Software\LionGrep hive — settings, recents, presets, last form.
        RegistryStore.DeleteAll();
        Presets.Clear();
        SelectedPreset = null;
        EditorCommand = "";
        DontWarnWhenReplacing = false;
        RememberRecentValues = true;
    }
}
