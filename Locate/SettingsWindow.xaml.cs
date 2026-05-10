using System.Runtime.InteropServices;
using Locate.Controls;
using Locate.Services;
using Locate.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Locate;

public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    private readonly nint _ownerHwnd;

    public SettingsWindow(nint ownerHwnd)
    {
        ViewModel = new SettingsViewModel(new SettingsStore(), new PresetsStore());
        InitializeComponent();
        // Bumped from 820×600 so the preset details panel — name, hotkey, three apply-group
        // checkboxes, and the explanatory caption — fits without a scrollbar at default DPI.
        AppWindow.Resize(new SizeInt32(1040, 720));

        // Disable the owner so this window behaves modally — clicks on the owner are eaten until
        // the user closes Settings. Re-enable when we close. Mirrors the ContentDialog UX of the
        // About dialog ("Settings should be modal and Escape should close it").
        _ownerHwnd = ownerHwnd;
        if (_ownerHwnd != 0) EnableWindow(_ownerHwnd, false);
        Closed += (_, _) =>
        {
            if (_ownerHwnd != 0)
            {
                EnableWindow(_ownerHwnd, true);
                SetForegroundWindow(_ownerHwnd);
            }
        };
    }

    /// <summary>Parameterless ctor kept so the XAML compiler stays happy; production should
    /// always go through the owner-aware overload.</summary>
    public SettingsWindow() : this(ownerHwnd: 0) { }

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

    private void OnEscapePressed(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Escape mirrors the Close button — save and close. Same gesture the About dialog uses.
        args.Handled = true;
        ViewModel.SaveAll();
        Close();
    }

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(nint hWnd, bool bEnable);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    private async void OnAssignHotkeyClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPreset is null) return;
        var captured = await HotkeyCaptureDialog.CaptureAsync(Content.XamlRoot);
        if (captured is not null)
            ViewModel.SelectedPreset.Hotkey = captured;
    }

    private async void OnResetEverythingClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Reset everything?",
            Content = "This erases all Locate settings, presets, and recently used values "
                    + "(including the registry data behind them). This cannot be undone.",
            PrimaryButtonText = "Reset everything",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ViewModel.ResetEverything();
    }
}
