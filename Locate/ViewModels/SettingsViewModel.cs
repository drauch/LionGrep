using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Locate.Core.Logic;
using Locate.Models;
using Locate.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Locate.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;
    private readonly PresetsStore _presetsStore;

    public SettingsViewModel(SettingsStore settingsStore, PresetsStore presetsStore)
    {
        _settingsStore = settingsStore;
        _presetsStore = presetsStore;

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

    [ObservableProperty] private string _editorCommand = "";
    [ObservableProperty] private bool _dontWarnWhenReplacing;
    [ObservableProperty] private bool _rememberRecentValues = true;

    public ObservableCollection<Preset> Presets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPreset))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetNameError))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetNameErrorVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetNameBorderBrush))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetHotkeyError))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetHotkeyErrorVisibility))]
    [NotifyPropertyChangedFor(nameof(SelectedPresetHotkeyBorderBrush))]
    [NotifyCanExecuteChangedFor(nameof(RemovePresetCommand))]
    private Preset? _selectedPreset;

    public bool HasSelectedPreset => SelectedPreset is not null;

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
            if (HotkeyParser.IsReserved(hk)) return "Reserved by Locate (Search / Replace).";
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
        if (e.PropertyName == nameof(Preset.Name))
        {
            OnPropertyChanged(nameof(SelectedPresetNameError));
            OnPropertyChanged(nameof(SelectedPresetNameErrorVisibility));
            OnPropertyChanged(nameof(SelectedPresetNameBorderBrush));
        }
        if (e.PropertyName == nameof(Preset.Hotkey))
        {
            OnPropertyChanged(nameof(SelectedPresetHotkeyError));
            OnPropertyChanged(nameof(SelectedPresetHotkeyErrorVisibility));
            OnPropertyChanged(nameof(SelectedPresetHotkeyBorderBrush));
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
        var preset = new Preset { Name = "New preset" };
        Presets.Add(preset);
        SelectedPreset = preset;
        SavePresets();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
    private void RemovePreset()
    {
        if (SelectedPreset is null) return;
        Presets.Remove(SelectedPreset);
        SelectedPreset = null;
        SavePresets();
    }

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
        // Wipe the entire HKCU\Software\Locate hive — settings, recents, presets, last form.
        RegistryStore.DeleteAll();
        Presets.Clear();
        SelectedPreset = null;
        EditorCommand = "";
        DontWarnWhenReplacing = false;
        RememberRecentValues = true;
    }
}
