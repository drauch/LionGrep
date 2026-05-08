using System.Text;
using Locate.Models;
using Locate.Services;
using Locate.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Locate;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly SettingsStore _settingsStore = new();
    private readonly PresetsStore _presetsStore = new();
    private readonly RecentsStore _recentsStore = new();
    private readonly EditorLauncher _editorLauncher = new();

    public MainWindow()
    {
        ViewModel = new MainViewModel(DispatcherQueue, _recentsStore, _settingsStore, _presetsStore);

        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1280, 900));
        RegisterPresetHotkeys();
    }

    // ---- Top-bar buttons ----
    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow();
        window.Closed += (_, _) =>
        {
            ViewModel.ReloadPresets();
            RegisterPresetHotkeys();
        };
        window.Activate();
    }

    private void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog { XamlRoot = Content.XamlRoot };
        _ = dialog.ShowAsync();
    }

    // ---- Where: browse + recents ----
    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        ViewModel.SearchIn = string.IsNullOrWhiteSpace(ViewModel.SearchIn)
            ? folder.Path
            : ViewModel.SearchIn.TrimEnd('\r', '\n') + Environment.NewLine + folder.Path;
    }

    private void OnSearchInRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout((Button)sender, MainViewModel.RecentsKeySearchIn, v => ViewModel.SearchIn = v);

    private void OnSearchPatternRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout((Button)sender, MainViewModel.RecentsKeySearchPattern, v => ViewModel.SearchPattern = v);

    private void OnReplacePatternRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout((Button)sender, MainViewModel.RecentsKeyReplacePattern, v => ViewModel.ReplacePattern = v);

    private void OnFileNamesRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout((Button)sender, MainViewModel.RecentsKeyFileNames, v => ViewModel.FileNames = v);

    private void OnExcludePathsRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout((Button)sender, MainViewModel.RecentsKeyExcludePaths, v => ViewModel.ExcludePaths = v);

    private void ShowRecentsFlyout(Button anchor, string fieldKey, Action<string> apply)
    {
        var items = _recentsStore.Get(fieldKey);
        var flyout = new MenuFlyout();
        if (items.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "(no recent values)", IsEnabled = false });
        }
        else
        {
            foreach (var item in items)
            {
                var captured = item;
                var fi = new MenuFlyoutItem { Text = TruncateForMenu(captured) };
                fi.Click += (_, _) => apply(captured);
                flyout.Items.Add(fi);
            }
        }
        flyout.ShowAt(anchor);
    }

    private static string TruncateForMenu(string s)
    {
        s = s.Replace('\r', ' ').Replace('\n', ' ');
        return s.Length > 80 ? s[..77] + "…" : s;
    }

    // ---- Results: expand-all + double-click + copy ----
    private async void OnExpandAllClicked(object sender, RoutedEventArgs e)
    {
        var expand = ((ToggleButton)sender).IsChecked == true;
        await ViewModel.ExpandAllAsync(expand);
    }

    private void OnLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LineMatchViewModel line })
        {
            LaunchEditor(line.FilePath, line.LineNumber, line.Column + 1);
            e.Handled = true;
        }
    }

    private void OnFileRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileMatchViewModel file })
        {
            var first = file.Lines.FirstOrDefault();
            if (first is not null)
                LaunchEditor(first.FilePath, first.LineNumber, first.Column + 1);
            else
                LaunchEditor(file.Path, 1, 1);
            e.Handled = true;
        }
    }

    private void LaunchEditor(string path, int line, int column)
    {
        var settings = _settingsStore.Load();
        if (!_editorLauncher.TryLaunch(settings.EditorCommand, path, line, column, out var error))
            ShowError("Could not launch editor", error ?? "Unknown error.");
    }

    private void OnCopyAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CopyToClipboard(BuildCsv(SelectedFiles()));
        args.Handled = true;
    }

    private void OnCopyName(object sender, RoutedEventArgs e) =>
        CopyToClipboard(string.Join(Environment.NewLine, SelectedFiles().Select(f => f.FileName)));

    private void OnCopyFullPath(object sender, RoutedEventArgs e) =>
        CopyToClipboard(string.Join(Environment.NewLine, SelectedFiles().Select(f => f.Path)));

    private void OnCopyLine(object sender, RoutedEventArgs e)
    {
        var lines = SelectedFiles()
            .SelectMany(f => f.Lines.Select(l => l.LineText));
        CopyToClipboard(string.Join(Environment.NewLine, lines));
    }

    private void OnCopyCsv(object sender, RoutedEventArgs e) => CopyToClipboard(BuildCsv(SelectedFiles()));

    private IReadOnlyList<FileMatchViewModel> SelectedFiles()
    {
        var selected = ResultsList.SelectedItems.OfType<FileMatchViewModel>().ToList();
        if (selected.Count == 0)
            selected = ViewModel.Results.ToList();
        return selected;
    }

    private static string BuildCsv(IReadOnlyList<FileMatchViewModel> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Path,Line,Column,Text");
        foreach (var f in files)
        {
            if (f.Lines.Count == 0)
            {
                sb.Append(CsvEscape(f.FileName)).Append(',')
                  .Append(CsvEscape(f.Path)).Append(',')
                  .Append(',').Append(',')
                  .AppendLine();
                continue;
            }
            foreach (var l in f.Lines)
            {
                sb.Append(CsvEscape(f.FileName)).Append(',')
                  .Append(CsvEscape(f.Path)).Append(',')
                  .Append(l.LineNumber).Append(',')
                  .Append(l.Column + 1).Append(',')
                  .Append(CsvEscape(l.LineText))
                  .AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string CsvEscape(string value)
    {
        if (value is null) return "";
        var needsQuotes = value.AsSpan().IndexOfAny(",\"\r\n".AsSpan()) >= 0;
        if (!needsQuotes) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // ---- Presets ----
    private void OnPresetsFlyoutOpening(object sender, object e)
    {
        ViewModel.ReloadPresets();
        var flyout = PresetsFlyout;
        flyout.Items.Clear();

        if (ViewModel.Presets.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "(no presets yet)", IsEnabled = false });
        }
        else
        {
            foreach (var preset in ViewModel.Presets)
            {
                var item = new MenuFlyoutItem
                {
                    Text = string.IsNullOrEmpty(preset.Hotkey) ? preset.Name : $"{preset.Name}  ({preset.Hotkey})",
                };
                var captured = preset;
                item.Click += (_, _) => ViewModel.ApplyPreset(captured);
                flyout.Items.Add(item);
            }
        }
        flyout.Items.Add(new MenuFlyoutSeparator());

        var saveAs = new MenuFlyoutItem { Text = "Save current as preset…", Icon = new FontIcon { Glyph = "" } };
        saveAs.Click += async (_, _) => await SaveCurrentAsPresetAsync();
        flyout.Items.Add(saveAs);

        var edit = new MenuFlyoutItem { Text = "Edit…", Icon = new FontIcon { Glyph = "" } };
        edit.Click += (_, _) => OnSettingsClicked(this, new RoutedEventArgs());
        flyout.Items.Add(edit);
    }

    private async Task SaveCurrentAsPresetAsync()
    {
        var input = new TextBox { PlaceholderText = "Preset name", AcceptsReturn = false };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Save current as preset",
            Content = input,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;
        var name = input.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var preset = ViewModel.SnapshotAsPreset(name);
        var presets = _presetsStore.Load().ToList();
        presets.Add(preset);
        _presetsStore.Save(presets);
        ViewModel.ReloadPresets();
        RegisterPresetHotkeys();
    }

    private void RegisterPresetHotkeys()
    {
        if (Content is not Grid root) return;
        root.KeyboardAccelerators.Clear();
        foreach (var preset in ViewModel.Presets)
        {
            if (string.IsNullOrEmpty(preset.Hotkey)) continue;
            if (!TryParseHotkey(preset.Hotkey, out var key, out var modifiers)) continue;

            var accel = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            var captured = preset;
            accel.Invoked += (_, args) =>
            {
                ViewModel.ApplyPreset(captured);
                args.Handled = true;
            };
            root.KeyboardAccelerators.Add(accel);
        }
    }

    private static bool TryParseHotkey(string hotkey, out VirtualKey key, out VirtualKeyModifiers modifiers)
    {
        key = VirtualKey.None;
        modifiers = VirtualKeyModifiers.None;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => VirtualKeyModifiers.Control,
                "alt" => VirtualKeyModifiers.Menu,
                "shift" => VirtualKeyModifiers.Shift,
                "win" or "windows" => VirtualKeyModifiers.Windows,
                _ => VirtualKeyModifiers.None,
            };
        }

        var last = parts[^1];
        if (last.Length == 1)
        {
            var c = char.ToUpperInvariant(last[0]);
            if (c is >= '0' and <= '9') { key = VirtualKey.Number0 + (c - '0'); return true; }
            if (c is >= 'A' and <= 'Z') { key = VirtualKey.A + (c - 'A'); return true; }
        }
        if (last.StartsWith('F') && int.TryParse(last[1..], out var fn) && fn is >= 1 and <= 24)
        {
            key = VirtualKey.F1 + (fn - 1);
            return true;
        }
        return false;
    }

    private void ShowError(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK",
        };
        _ = dialog.ShowAsync();
    }
}
