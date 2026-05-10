using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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

    public SettingsWindow(nint ownerHwnd, Locate.Models.Preset? addTemplate = null)
    {
        ViewModel = new SettingsViewModel(new SettingsStore(), new PresetsStore(), addTemplate);
        InitializeComponent();
        // Bumped from 820×600 so the preset details panel — name, hotkey, three apply-group
        // checkboxes, and the explanatory caption — fits without a scrollbar at default DPI.
        AppWindow.Resize(new SizeInt32(1040, 720));

        _ownerHwnd = ownerHwnd;
        if (_ownerHwnd != 0)
        {
            // Two-step modality: (1) set the owner HWND via GWLP_HWNDPARENT so this window stays
            // z-ordered above the main window, doesn't get its own taskbar entry, and minimizes
            // with the main window; (2) disable the owner so clicks on it are eaten while Settings
            // is open. Together this makes Settings behave like a true modal dialog rather than a
            // peer top-level window.
            var thisHwnd = WindowNative.GetWindowHandle(this);
            SetWindowLongPtr(thisHwnd, GWLP_HWNDPARENT, _ownerHwnd);
            EnableWindow(_ownerHwnd, false);
        }
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
        // Save commits in-memory edits to the registry, then closes.
        ViewModel.SaveAll();
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        // Cancel discards every change made since the dialog opened — settings, preset edits,
        // additions, removals, hotkey reassignments, the lot. Possible because AddPreset and
        // RemovePreset no longer persist eagerly; only Save touches the registry.
        Close();
    }

    private void OnEscapePressed(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Esc behaves like Cancel — matches Windows convention and matches the user's mental
        // model of "Cancel undoes". Use Save explicitly to keep your edits.
        args.Handled = true;
        Close();
    }

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(nint hWnd, bool bEnable);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    // Win32 GWLP_HWNDPARENT (-8) sets a window's owner — different from a child-parent. Owned
    // windows stay above their owner in z-order and share its taskbar entry.
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    private const int GWLP_HWNDPARENT = -8;

    private async void OnRemovePresetClicked(object sender, RoutedEventArgs e)
    {
        var preset = ViewModel.SelectedPreset;
        if (preset is null) return;
        var displayName = string.IsNullOrWhiteSpace(preset.Name) ? "(unnamed)" : preset.Name;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Remove preset?",
            Content = $"Remove the preset \"{displayName}\"? This can't be undone.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ViewModel.RemovePresetCommand.Execute(null);
    }

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

    // ---- Export / import ----
    //
    // The export format is a Windows Registry .reg file (plain text, UTF-16 LE BOM, with the
    // Locate registry root captured verbatim). We save it with a `.reg.ini` extension so a stray
    // double-click can't trigger regedit and overwrite the user's live registry; to actually
    // import it the user either renames to `.reg` and double-clicks, or uses the Import button
    // here (which warns first and wipes the existing root).
    //
    // Round-trip honors --alternate-registry-key: the .reg file embeds the absolute root path it
    // was exported from, so import always lands the values back in the same root.

    private async void OnExportSettingsClicked(object sender, RoutedEventArgs e)
    {
        // First save the current in-memory edits so the export reflects what the user sees, not
        // a stale snapshot from before they opened Settings.
        ViewModel.SaveAll();

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "locate-settings",
            DefaultFileExtension = ".reg",
        };
        picker.FileTypeChoices.Add("Registry file", new System.Collections.Generic.List<string> { ".reg" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        var tempReg = Path.Combine(Path.GetTempPath(), $"locate-export-{System.Guid.NewGuid():N}.reg");
        try
        {
            var (ok, stderr) = await RunRegExeAsync($"export \"HKCU\\{RegistryStore.RootPath}\" \"{tempReg}\" /y");
            if (!ok)
            {
                await ShowMessageAsync("Export failed", stderr.Length > 0 ? stderr : "reg.exe returned a non-zero exit code.");
                return;
            }
            File.Copy(tempReg, file.Path, overwrite: true);
            await ShowMessageAsync("Settings exported",
                $"Saved to \"{file.Path}\".\n\nTo restore on another machine, double-click the file " +
                "(Windows imports it via regedit) or use Import settings… here.");
        }
        catch (System.Exception ex)
        {
            await ShowMessageAsync("Export failed", ex.Message);
        }
        finally
        {
            try { File.Delete(tempReg); } catch { /* best effort */ }
        }
    }

    private async void OnImportSettingsClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".reg");
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Import settings?",
            Content = $"This will overwrite ALL of your current Locate settings — presets, recently used "
                    + $"values, the editor command, and the last-form snapshot — with the contents of "
                    + $"\"{file.Name}\". This cannot be undone.",
            PrimaryButtonText = "Overwrite and import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var tempReg = Path.Combine(Path.GetTempPath(), $"locate-import-{System.Guid.NewGuid():N}.reg");
        try
        {
            // Copy to a .reg-extension temp because reg.exe import doesn't care about the extension
            // but some Windows builds reject it; copying is also defensive against the source being
            // on a read-only or non-local file system.
            File.Copy(file.Path, tempReg, overwrite: true);

            // Wipe the existing root so the import is a true overwrite, not a merge.
            RegistryStore.DeleteAll();

            var (ok, stderr) = await RunRegExeAsync($"import \"{tempReg}\"");
            if (!ok)
            {
                await ShowMessageAsync("Import failed", stderr.Length > 0 ? stderr : "reg.exe returned a non-zero exit code.");
                return;
            }
            await ShowMessageAsync("Settings imported",
                "Restart Locate for the imported settings to take effect.");
            // Close Settings to prevent the in-memory (now stale) VM from overwriting the
            // freshly-imported registry on Save. Anything the user had been editing in this
            // session is replaced anyway.
            Close();
        }
        catch (System.Exception ex)
        {
            await ShowMessageAsync("Import failed", ex.Message);
        }
        finally
        {
            try { File.Delete(tempReg); } catch { /* best effort */ }
        }
    }

    /// <summary>Runs reg.exe with the given arguments and returns (success, captured stderr).
    /// Hidden window, redirected output so a ConsoleHost flash doesn't appear during export/import.</summary>
    private static async Task<(bool ok, string stderr)> RunRegExeAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi)!;
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode == 0, await stderrTask);
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "OK",
        };
        await dlg.ShowAsync();
    }
}
