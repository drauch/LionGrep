using Locate.Services;
using Locate.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Locate;

public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow()
    {
        ViewModel = new SettingsViewModel(new SettingsStore(), new PresetsStore());
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(820, 600));
    }

    private async void OnBrowseEditorClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        ViewModel.EditorCommand = $"\"{file.Path}\" \"%path%\":%line%:%column%";
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveAll();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveAll();
        Close();
    }
}
