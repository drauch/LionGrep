using System.Runtime.InteropServices;
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

    private readonly nint _windowHandle;
    private readonly NativeMethods.SubclassProc _subclassProc;
    private int _lastBreakpoint = -1;

    public MainWindow()
    {
        ViewModel = new MainViewModel(DispatcherQueue, _recentsStore, _settingsStore, _presetsStore);

        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1280, 900));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _windowHandle = WindowNative.GetWindowHandle(this);
        _subclassProc = SubclassProc;
        NativeMethods.SetWindowSubclass(_windowHandle, _subclassProc, 1, 0);

        ViewModel.OperationStarted += (_, _) => CollapseFormRow();

        Activated += OnFirstActivated;
        if (Content is FrameworkElement root)
            root.SizeChanged += OnRootSizeChanged;

        RegisterPresetHotkeys();
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        if (Content is FrameworkElement root)
            ApplyBreakpointFor(root.ActualWidth);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyBreakpointFor(e.NewSize.Width);
    }

    private void ApplyBreakpointFor(double width)
    {
        var bp = width switch
        {
            < 600 => 0,
            < 700 => 1,
            < 800 => 2,
            < 900 => 3,
            < 1000 => 4,
            < 1200 => 5,
            _ => 6,
        };
        if (bp == _lastBreakpoint) return;
        _lastBreakpoint = bp;

        // Hide priority (most aggressive first): Date → Path → Size → Encoding → Ext → Matches.
        ViewModel.MatchesWidth = bp >= 1 ? new GridLength(70) : new GridLength(0);
        ViewModel.ExtWidth = bp >= 2 ? new GridLength(50) : new GridLength(0);
        ViewModel.EncodingWidth = bp >= 3 ? new GridLength(80) : new GridLength(0);
        ViewModel.SizeWidth = bp >= 4 ? new GridLength(70) : new GridLength(0);
        ViewModel.PathWidth = bp >= 5 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ViewModel.DateWidth = bp >= 6 ? new GridLength(140) : new GridLength(0);
        // Name is always visible
        ViewModel.NameWidth = new GridLength(240);

        // Stack Size/Date below the file-name pair when narrow.
        if (bp <= 1)
        {
            FilterLeftCol.Width = new GridLength(1, GridUnitType.Star);
            FilterRightCol.Width = new GridLength(0);
            Grid.SetColumn(SizeDateGrid, 0);
            Grid.SetRow(SizeDateGrid, 2);
            EnsureFilterRow2();
        }
        else
        {
            FilterLeftCol.Width = new GridLength(1, GridUnitType.Star);
            FilterRightCol.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(SizeDateGrid, 1);
            Grid.SetRow(SizeDateGrid, 0);
        }
    }

    private bool _filterRow2Added;
    private void EnsureFilterRow2()
    {
        if (_filterRow2Added) return;
        FilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _filterRow2Added = true;
    }

    private void CollapseFormRow()
    {
        FormRow.Height = new GridLength(0);
    }

    private nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint subclassId, nuint refData)
    {
        if (msg == NativeMethods.WM_GETMINMAXINFO)
        {
            unsafe
            {
                var mmi = (NativeMethods.MINMAXINFO*)lParam;
                var dpi = NativeMethods.GetDpiForWindow(hWnd);
                var scale = dpi == 0 ? 1.0 : dpi / 96.0;
                mmi->ptMinTrackSize.x = (int)(500 * scale);
                mmi->ptMinTrackSize.y = (int)(360 * scale);
            }
        }
        return NativeMethods.DefSubclassProc(hWnd, msg, wParam, lParam);
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
        InitializeWithWindow.Initialize(picker, _windowHandle);
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

    // ---- Column resize thumbs ----
    private void OnNameThumbDragDelta(object sender, DragDeltaEventArgs e)
        => ViewModel.NameWidth = ResizeWidth(ViewModel.NameWidth, e.HorizontalChange);
    private void OnSizeThumbDragDelta(object sender, DragDeltaEventArgs e)
        => ViewModel.SizeWidth = ResizeWidth(ViewModel.SizeWidth, e.HorizontalChange);
    private void OnMatchesThumbDragDelta(object sender, DragDeltaEventArgs e)
        => ViewModel.MatchesWidth = ResizeWidth(ViewModel.MatchesWidth, e.HorizontalChange);
    private void OnPathThumbDragDelta(object sender, DragDeltaEventArgs e)
        => ViewModel.ExtWidth = ResizeWidth(ViewModel.ExtWidth, -e.HorizontalChange); // Path is *; widen/narrow Ext to expand/shrink Path indirectly.
    private void OnExtThumbDragDelta(object sender, DragDeltaEventArgs e)
        => ViewModel.ExtWidth = ResizeWidth(ViewModel.ExtWidth, e.HorizontalChange);
    private void OnEncodingThumbDragDelta(object sender, DragDeltaEventArgs e)
        => ViewModel.EncodingWidth = ResizeWidth(ViewModel.EncodingWidth, e.HorizontalChange);

    private static GridLength ResizeWidth(GridLength current, double delta)
    {
        if (!current.IsAbsolute) return current;
        return new GridLength(Math.Max(30, current.Value + delta));
    }

    // ---- Results: expand-all + double-click + copy ----
    private async void OnExpandAllClicked(object sender, RoutedEventArgs e) => await ViewModel.ExpandAllAsync(true);
    private async void OnCollapseAllClicked(object sender, RoutedEventArgs e) => await ViewModel.ExpandAllAsync(false);

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

        var saveAs = new MenuFlyoutItem { Text = "Save current as preset…" };
        saveAs.Click += async (_, _) => await SaveCurrentAsPresetAsync();
        flyout.Items.Add(saveAs);

        var edit = new MenuFlyoutItem { Text = "Edit…" };
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

internal static class NativeMethods
{
    public const uint WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    public delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint subclassId, nuint refData);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint subclassId, nuint refData);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    public static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hwnd);
}
