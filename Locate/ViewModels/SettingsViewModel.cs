using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Locate.Models;
using Locate.Services;

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

        foreach (var p in _presetsStore.Load())
            Presets.Add(p);
    }

    [ObservableProperty] private string _editorCommand = "";
    [ObservableProperty] private bool _dontWarnWhenReplacing;

    public ObservableCollection<Preset> Presets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPreset))]
    [NotifyCanExecuteChangedFor(nameof(RemovePresetCommand))]
    private Preset? _selectedPreset;

    public bool HasSelectedPreset => SelectedPreset is not null;

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
        _settingsStore.Save(new AppSettings
        {
            EditorCommand = EditorCommand,
            DontWarnWhenReplacing = DontWarnWhenReplacing,
        });
        SavePresets();
    }

    public void SavePresets() => _presetsStore.Save([.. Presets]);
}
