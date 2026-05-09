using System.Text;
using Locate.Core.Logic;
using Locate.Models;
using Locate.Services;
using Locate.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
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

    private readonly List<KeyboardAccelerator> _presetAccelerators = [];

    private string? _sortColumn;
    private SortDirection _sortDirection;
    private bool _initialDefaultSortApplied;
    private bool _initialFormFitDone;
    private double _cachedFormHeight;

    public MainWindow()
    {
        ViewModel = new MainViewModel(DispatcherQueue, _recentsStore, _settingsStore, _presetsStore);

        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1440, 900));

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _windowHandle = WindowNative.GetWindowHandle(this);
        _subclassProc = SubclassProc;
        NativeMethods.SetWindowSubclass(_windowHandle, _subclassProc, 1, 0);

        ViewModel.OperationStarted += (_, _) => CollapseFormRow();
        ViewModel.SearchCompleted += (_, _) => OnSearchCompleted();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.WindowTitle))
                Title = ViewModel.WindowTitle;
        };

        Activated += OnFirstActivated;
        if (Content is FrameworkElement root)
            root.SizeChanged += OnRootSizeChanged;

        // Catch Ctrl+Enter / Ctrl+Alt+Enter even when a TextBox has marked the KeyDown handled.
        RootGrid.AddHandler(UIElement.KeyDownEvent,
            new KeyEventHandler(OnRootKeyDownIntercept),
            handledEventsToo: true);

        WireInputDoubleTaps();
        RegisterPresetHotkeys();
    }

    private void OnRootKeyDownIntercept(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                    & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (!ctrl) return;
        var alt = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
                   & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        e.Handled = true;
        // Ctrl+Alt+Enter is the explicit "replace immediately" bypass — no confirmation dialog.
        if (alt)
        {
            if (ViewModel.ReplaceCommand.CanExecute(null))
                ViewModel.ReplaceCommand.Execute(null);
        }
        else if (ViewModel.SearchCommand.CanExecute(null))
            ViewModel.SearchCommand.Execute(null);
    }

    private void WireInputDoubleTaps()
    {
        Wire(SearchInBox, MainViewModel.RecentsKeySearchIn, v => ViewModel.SearchIn = v);
        Wire(SearchPatternBox, MainViewModel.RecentsKeySearchPattern, v => ViewModel.SearchPattern = v);
        Wire(ReplacePatternBox, MainViewModel.RecentsKeyReplacePattern, v => ViewModel.ReplacePattern = v);
        Wire(FileNamesBox, MainViewModel.RecentsKeyFileNames, v => ViewModel.FileNames = v);
        Wire(ExcludePathsBox, MainViewModel.RecentsKeyExcludePaths, v => ViewModel.ExcludePaths = v);

        void Wire(TextBox box, string key, Action<string> apply)
        {
            // TextBox marks DoubleTapped Handled (for word selection) — register with handledEventsToo so we still see it.
            box.AddHandler(UIElement.DoubleTappedEvent,
                new DoubleTappedEventHandler((_, _) =>
                {
                    // Defer so word-selection doesn't fight the flyout focus.
                    DispatcherQueue.TryEnqueue(() => ShowRecentsFlyout(box, key, apply));
                }),
                handledEventsToo: true);
        }
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        if (Content is FrameworkElement root)
            ApplyBreakpointFor(root.ActualWidth);
    }

    private void OnFormStackPanelLoaded(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            FitFormRow();
            SearchPatternBox.Focus(FocusState.Programmatic);
        });
    }

    private void FitFormRow()
    {
        if (_initialFormFitDone) return;
        var width = FormStackPanel.ActualWidth > 0 ? FormStackPanel.ActualWidth : 1100;
        FormStackPanel.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        var height = FormStackPanel.DesiredSize.Height;
        if (height <= 0) return;
        _initialFormFitDone = true;
        _cachedFormHeight = height + 56;
        FormRow.Height = new GridLength(_cachedFormHeight);
        UpdateShowQueryButtonVisibility(FormAreaGrid.ActualHeight);
    }

    private void OnSearchCompleted()
    {
        if (!_initialDefaultSortApplied)
        {
            _initialDefaultSortApplied = true;
            if (_sortColumn is null)
            {
                _sortColumn = "Path";
                _sortDirection = SortDirection.Ascending;
            }
        }
        ApplySort();
        UpdateHeaderTexts();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyBreakpointFor(e.NewSize.Width);
    }

    private void ApplyBreakpointFor(double width)
    {
        var bp = ResponsiveLayout.GetBreakpoint(width);
        if (bp == _lastBreakpoint) return;
        _lastBreakpoint = bp;

        var widths = ResponsiveLayout.GetColumnWidths(bp);
        ViewModel.NameWidth     = ToGridLength(widths.Name);
        ViewModel.SizeWidth     = ToGridLength(widths.Size);
        ViewModel.MatchesWidth  = ToGridLength(widths.Matches);
        ViewModel.PathWidth     = widths.PathStretch ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ViewModel.ExtWidth      = ToGridLength(widths.Ext);
        ViewModel.EncodingWidth = ToGridLength(widths.Encoding);
        ViewModel.DateWidth     = ToGridLength(widths.Date);

        if (widths.StackSizeDateBelowFileName)
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

    private static GridLength ToGridLength(int pixels) => pixels <= 0 ? new GridLength(0) : new GridLength(pixels);

    private bool _filterRow2Added;
    private void EnsureFilterRow2()
    {
        if (_filterRow2Added) return;
        FilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _filterRow2Added = true;
    }

    private void CollapseFormRow()
    {
        // Use Star (with 0 weight) so the GridSplitter can drag the row back to a positive size.
        FormRow.Height = new GridLength(0, GridUnitType.Star);
    }

    private void OnFormAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateShowQueryButtonVisibility(e.NewSize.Height);
    }

    private void UpdateShowQueryButtonVisibility(double formAreaHeight)
    {
        if (ShowQueryButton is null) return;
        if (_cachedFormHeight <= 0)
        {
            // Natural form height not yet measured — keep hidden until first FitFormRow runs.
            ShowQueryButton.Visibility = Visibility.Collapsed;
            return;
        }
        // Show only when the form area is meaningfully smaller than its natural full height.
        var collapsed = formAreaHeight < _cachedFormHeight - 8;
        ShowQueryButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
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
        var window = new SettingsWindow(_windowHandle);
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

        // Browse picks ONE folder, so it should produce a single-folder Search-in. Replace whatever
        // multi-line content was there and collapse the box back to single-line view — the picker
        // is the "I want exactly this folder" gesture, not the "add another" gesture.
        ViewModel.SearchIn = folder.Path;
        ViewModel.IsSearchInExpanded = false;
    }

    private void OnSearchInRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout(SearchInBox, MainViewModel.RecentsKeySearchIn, v => ViewModel.SearchIn = v);

    private void OnSearchPatternRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout(SearchPatternBox, MainViewModel.RecentsKeySearchPattern, v => ViewModel.SearchPattern = v);

    private void OnReplacePatternRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout(ReplacePatternBox, MainViewModel.RecentsKeyReplacePattern, v => ViewModel.ReplacePattern = v);

    private void OnFileNamesRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout(FileNamesBox, MainViewModel.RecentsKeyFileNames, v => ViewModel.FileNames = v);

    private void OnExcludePathsRecentsClicked(object sender, RoutedEventArgs e) =>
        ShowRecentsFlyout(ExcludePathsBox, MainViewModel.RecentsKeyExcludePaths, v => ViewModel.ExcludePaths = v);

    private void ShowRecentsFlyout(FrameworkElement anchor, string fieldKey, Action<string> apply)
    {
        var items = _recentsStore.Get(fieldKey).Take(10).ToList();
        var width = anchor.ActualWidth > 0 ? anchor.ActualWidth : 240;

        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            Width = width,
            MaxHeight = 240,
            FontSize = 12,
            Padding = new Thickness(0),
        };
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 1, 6, 1)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        listView.ItemContainerStyle = itemStyle;

        if (items.Count == 0)
        {
            listView.IsItemClickEnabled = false;
            listView.Items.Add(new TextBlock { Text = "(no recent values)", FontSize = 12, Opacity = 0.6, Padding = new Thickness(6, 2, 6, 2) });
        }
        else
        {
            foreach (var s in items)
                listView.Items.Add(new TextBlock { Text = s.Replace('\r', ' ').Replace('\n', ' '), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var flyout = new Flyout
        {
            Content = listView,
            ShouldConstrainToRootBounds = true,
        };
        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        presenterStyle.Setters.Add(new Setter(Control.MaxWidthProperty, double.PositiveInfinity));
        flyout.FlyoutPresenterStyle = presenterStyle;

        listView.ItemClick += (_, e) =>
        {
            var idx = listView.Items.IndexOf(e.ClickedItem);
            if (idx >= 0 && idx < items.Count)
                apply(items[idx]);
            flyout.Hide();
        };
        flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });
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

    // ---- Results: expand-all + double-click + copy + open ----
    private async void OnExpandAllClicked(object sender, RoutedEventArgs e) => await ViewModel.ExpandAllAsync(true);
    private async void OnCollapseAllClicked(object sender, RoutedEventArgs e) => await ViewModel.ExpandAllAsync(false);
    private void OnShowQueryClicked(object sender, RoutedEventArgs e) => RestoreFormRow();

    private void OnSearchInResultsToggled(object sender, RoutedEventArgs e)
    {
        // After the binding has updated IsResultFilterPanelOpen, focus the textbox if we just opened.
        if (sender is ToggleButton { IsChecked: true })
        {
            DispatcherQueue.TryEnqueue(() => FilterBox.Focus(FocusState.Programmatic));
        }
    }

    private void OnFilterBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            // Esc collapses the filter panel; the VM's OnIsResultFilterPanelOpenChanged also clears FilterText.
            ViewModel.IsResultFilterPanelOpen = false;
            e.Handled = true;
        }
    }

    private void OnFileRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FileMatchViewModel clicked }) return;

        // Only seed the selection from the right-clicked row when there is no current selection,
        // so existing multi-select isn't collapsed when the user right-clicks one of the selected rows.
        if (ResultsList.SelectedItems.Count == 0)
            ResultsList.SelectedItems.Add(clicked);
    }

    private async void OnOpenWithEditor(object sender, RoutedEventArgs e)
    {
        var selected = ExplicitlySelectedFiles();
        if (selected.Count == 0) return;

        var settings = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.EditorCommand))
        {
            ShowError("No editor configured", "Set an editor command in Settings (gear icon, top right).");
            return;
        }

        if (selected.Count > 1)
        {
            if (!await ConfirmBulkAsync($"Open {selected.Count:N0} files in editor?",
                $"This will launch {selected.Count:N0} editor windows.")) return;
        }
        foreach (var f in selected)
            OpenFileInEditor(f, settings.EditorCommand);
    }

    private async void OnExportToCsv(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "locate-results.csv" };
        picker.FileTypeChoices.Add("CSV (UTF-8)", new List<string> { ".csv" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        var csv = BuildCsv(ViewModel.FilteredResults.ToList());
        await File.WriteAllTextAsync(file.Path, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private async void OnOpenInExcel(object sender, RoutedEventArgs e)
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"locate-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
        try
        {
            WriteXlsx(temp, ViewModel.FilteredResults.ToList());
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = temp,
                UseShellExecute = true,
            });
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ShowError("Could not open in Excel", ex.Message);
        }
    }

    private static void WriteXlsx(string path, IReadOnlyList<FileMatchViewModel> files)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Locate results");
        ws.Cell(1, 1).Value = "Name";
        ws.Cell(1, 2).Value = "Path";
        ws.Cell(1, 3).Value = "Line";
        ws.Cell(1, 4).Value = "Column";
        ws.Cell(1, 5).Value = "Text";
        var header = ws.Range(1, 1, 1, 5);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromArgb(0xEE, 0xEE, 0xEE);

        var row = 2;
        foreach (var f in files)
        {
            if (f.Lines.Count == 0)
            {
                ws.Cell(row, 1).Value = f.FileName;
                ws.Cell(row, 2).Value = f.Path;
                row++;
                continue;
            }
            foreach (var l in f.Lines)
            {
                ws.Cell(row, 1).Value = f.FileName;
                ws.Cell(row, 2).Value = f.Path;
                ws.Cell(row, 3).Value = l.LineNumber;
                ws.Cell(row, 4).Value = l.Column + 1;
                ws.Cell(row, 5).Value = l.LineText;
                row++;
            }
        }
        ws.Columns(1, 5).AdjustToContents(1, Math.Min(row - 1, 200));
        wb.SaveAs(path);
    }

    private async void OnOpenContainingFolder(object sender, RoutedEventArgs e)
    {
        var selected = ExplicitlySelectedFiles();
        if (selected.Count == 0) return;
        if (selected.Count > 1)
        {
            if (!await ConfirmBulkAsync($"Open {selected.Count:N0} folders?",
                $"This will launch {selected.Count:N0} Explorer windows.")) return;
        }
        foreach (var f in selected)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{f.Path}\"");
            }
            catch { /* ignore */ }
        }
    }

    // ---- Hide the inner X / clear button on size NumberBoxes ----
    private void OnSizeNumberBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not NumberBox nb) return;
        // The inner TextBox's template (which contains the DeleteButton part) may not be applied
        // yet at NumberBox.Loaded. Defer to a later dispatcher tick so the part is realized.
        TryHideDeleteButton(nb, attemptsRemaining: 5);
    }

    private void TryHideDeleteButton(NumberBox nb, int attemptsRemaining)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            // Right-align the digits in the inner TextBox (named "InputBox" in NumberBox's template).
            if (FindDescendantByName(nb, "InputBox") is TextBox input)
                input.TextAlignment = TextAlignment.Right;

            var btn = FindDescendantByName(nb, "DeleteButton") as FrameworkElement;
            if (btn is null)
            {
                if (attemptsRemaining > 0) TryHideDeleteButton(nb, attemptsRemaining - 1);
                return;
            }
            SuppressDeleteButton(btn);
        });
    }

    private static void SuppressDeleteButton(FrameworkElement btn)
    {
        btn.Visibility = Visibility.Collapsed;
        btn.Width = 0;
        // The TextBox's VisualStateManager flips DeleteButton.Visibility back to Visible when the
        // ButtonVisible state activates (text present + focus). Force it Collapsed every time
        // someone sets it back. The callback only re-runs when the value actually changes, so no
        // recursion risk.
        btn.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (s, _) =>
        {
            if (s is FrameworkElement el && el.Visibility == Visibility.Visible)
                el.Visibility = Visibility.Collapsed;
        });
    }

    private static DependencyObject? FindDescendantByName(DependencyObject root, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return child;
            var result = FindDescendantByName(child, name);
            if (result is not null) return result;
        }
        return null;
    }

    // ---- Open with… (Windows shell "Open With" dialog) ----
    private async void OnOpenWithDialog(object sender, RoutedEventArgs e)
    {
        var selected = ExplicitlySelectedFiles();
        if (selected.Count == 0) return;
        if (selected.Count > 1)
        {
            if (!await ConfirmBulkAsync($"Open {selected.Count:N0} files with…?",
                $"This will open {selected.Count:N0} 'Open With' dialogs.")) return;
        }
        var hwnd = _windowHandle;
        foreach (var f in selected)
        {
            var path = f.Path;
            // Run on a thread-pool task so the UI thread keeps pumping messages while the
            // shell wires up the picker. Without this the wait-cursor sticks until the next
            // mouse move, since Windows only refreshes the cursor when the UI thread is free.
            _ = Task.Run(() =>
            {
                try
                {
                    var info = new NativeMethods.OPENASINFO
                    {
                        pcszFile = path,
                        pcszClass = null,
                        oaifInFlags = NativeMethods.OAIF_EXEC | NativeMethods.OAIF_HIDE_REGISTRATION,
                    };
                    NativeMethods.SHOpenWithDialog(hwnd, ref info);
                }
                catch { /* ignore */ }
            });
        }
    }

    // ---- Cut / Copy files (Explorer-compatible clipboard) ----
    private async void OnCutFiles(object sender, RoutedEventArgs e)
        => await PutFilesOnClipboardAsync(DataPackageOperation.Move);

    private async void OnCopyFiles(object sender, RoutedEventArgs e)
        => await PutFilesOnClipboardAsync(DataPackageOperation.Copy);

    private async Task PutFilesOnClipboardAsync(DataPackageOperation op)
    {
        var paths = ExplicitlySelectedFiles().Select(f => f.Path).ToList();
        if (paths.Count == 0) return;
        var pkg = new DataPackage { RequestedOperation = op };
        var items = new List<IStorageItem>();
        foreach (var p in paths)
        {
            try { items.Add(await StorageFile.GetFileFromPathAsync(p)); }
            catch { /* skip files we can't open as StorageFile */ }
        }
        if (items.Count == 0) return;
        pkg.SetStorageItems(items);
        Clipboard.SetContent(pkg);
        Clipboard.Flush();
    }

    // ---- Delete (to Recycle Bin) ----
    private async void OnDeleteFiles(object sender, RoutedEventArgs e)
    {
        var selected = ExplicitlySelectedFiles().ToList();
        if (selected.Count == 0) return;
        var msg = selected.Count == 1
            ? $"Move \"{selected[0].FileName}\" to the Recycle Bin?"
            : $"Move {selected.Count:N0} files to the Recycle Bin?";
        if (!await ConfirmBulkAsync("Delete", msg)) return;

        var deleted = new List<FileMatchViewModel>();
        foreach (var f in selected)
        {
            try
            {
                var sf = await StorageFile.GetFileFromPathAsync(f.Path);
                // StorageDeleteOption.Default = Recycle Bin (where supported); PermanentDelete bypasses it.
                await sf.DeleteAsync(StorageDeleteOption.Default);
                deleted.Add(f);
            }
            catch { /* file already gone, locked, etc. */ }
        }
        // Drop the now-deleted files from the live results list so the UI reflects reality.
        foreach (var f in deleted)
            ViewModel.Results.Remove(f);
    }

    // ---- Properties (Windows file properties dialog) ----
    private void OnShowProperties(object sender, RoutedEventArgs e)
    {
        var selected = ExplicitlySelectedFiles();
        if (selected.Count == 0) return;
        // SHObjectProperties is one-file-at-a-time; for multi-select, only the right-clicked / first one.
        var f = selected[0];
        try { NativeMethods.SHObjectProperties(_windowHandle, NativeMethods.SHOP_FILEPATH, f.Path, null); }
        catch { /* ignore */ }
    }

    private async Task<bool> ConfirmBulkAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void OnLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LineMatchViewModel line })
        {
            LaunchEditor(line.FilePath, line.LineNumber, line.Column + 1);
            e.Handled = true;
        }
    }

    private async void OnFileRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: FileMatchViewModel clicked }) return;

        var settings = _settingsStore.Load();
        if (string.IsNullOrWhiteSpace(settings.EditorCommand))
        {
            ShowError("No editor configured", "Set an editor command in Settings (gear icon, top right).");
            return;
        }

        var selected = ResultsList.SelectedItems.OfType<FileMatchViewModel>().ToList();
        // If the double-clicked row isn't part of the current selection, treat the click as targeting just it.
        if (!selected.Contains(clicked))
            selected = [clicked];

        if (selected.Count > 1)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = $"Open {selected.Count:N0} files?",
                Content = $"This will launch {selected.Count:N0} editor windows.",
                PrimaryButtonText = "Open all",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        foreach (var f in selected)
            OpenFileInEditor(f, settings.EditorCommand);
    }

    private void OpenFileInEditor(FileMatchViewModel file, string editorCommand)
    {
        string? error;
        var first = file.Lines.FirstOrDefault();
        var ok = first is not null
            ? _editorLauncher.TryLaunch(editorCommand, first.FilePath, first.LineNumber, first.Column + 1, out error)
            : _editorLauncher.TryLaunch(editorCommand, file.Path, 1, 1, out error);
        if (!ok)
            ShowError("Could not launch editor", error ?? "Unknown error.");
    }

    private void LaunchEditor(string path, int line, int column)
    {
        var settings = _settingsStore.Load();
        if (!_editorLauncher.TryLaunch(settings.EditorCommand, path, line, column, out var error))
            ShowError("Could not launch editor", error ?? "Unknown error.");
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

    // For copy/export commands: fall back to "all results" when nothing is selected — that's a useful default.
    private IReadOnlyList<FileMatchViewModel> SelectedFiles()
    {
        var selected = ResultsList.SelectedItems.OfType<FileMatchViewModel>().ToList();
        if (selected.Count == 0)
            // Default to the visible (filtered) set, not the master list, so a Copy/Export with
            // an active filter only acts on what the user can see.
            selected = ViewModel.FilteredResults.ToList();
        return selected;
    }

    // For destructive/launch commands (Open with editor, Open containing folder): never fall back to all.
    private IReadOnlyList<FileMatchViewModel> ExplicitlySelectedFiles() =>
        ResultsList.SelectedItems.OfType<FileMatchViewModel>().ToList();

    private static string BuildCsv(IReadOnlyList<FileMatchViewModel> files)
    {
        // Translate FileMatchViewModel into the WinUI-free DTO shape the testable CsvBuilder accepts.
        // l.Column + 1 keeps the human-friendly 1-based column the existing CSV exports use.
        var entries = files.Select(f => new CsvBuilder.FileEntry(
            f.FileName,
            f.Path,
            f.Lines.Select(l => new CsvBuilder.LineEntry(l.LineNumber, l.Column + 1, l.LineText)).ToList()));
        return CsvBuilder.Build(entries);
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
        // Only remove the accelerators we previously added; preserve XAML-defined ones (Enter, Ctrl+Enter).
        foreach (var accel in _presetAccelerators)
            root.KeyboardAccelerators.Remove(accel);
        _presetAccelerators.Clear();

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
            _presetAccelerators.Add(accel);
        }
    }

    // ---- Hotkeys: Ctrl+Enter = Search, Ctrl+Alt+Enter = Replace ----
    private void OnCtrlEnterAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ViewModel.SearchCommand.CanExecute(null))
            ViewModel.SearchCommand.Execute(null);
    }

    private void OnCtrlAltEnterAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        // Ctrl+Alt+Enter bypasses the 3-way confirmation dialog and replaces immediately (no .bak).
        if (ViewModel.ReplaceCommand.CanExecute(null))
            ViewModel.ReplaceCommand.Execute(null);
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        // Layered Escape, exactly one layer per press:
        //   1. If the filter panel is open, close it (the VM hook also clears FilterText). Done — the
        //      user must press Esc again to act on the next layer. This is intentional per the user's
        //      explicit request: "first Esc closes the filter, only the second Esc shows the form".
        //   2. Otherwise, if a search is in flight, cancel it AND restore the form row so the inputs
        //      are visible again — the cancel and the restore are the same atomic gesture.
        //   3. Otherwise, just restore the form row.
        if (ViewModel.IsResultFilterPanelOpen)
        {
            ViewModel.IsResultFilterPanelOpen = false;
            return;
        }
        if (ViewModel.IsSearching)
            ViewModel.CancelSearchCommand.Execute(null);
        RestoreFormRow();
    }

    private void RestoreFormRow()
    {
        // Idempotent: always set to the cached natural form height. Multiple presses don't grow it.
        if (_cachedFormHeight > 0)
            FormRow.Height = new GridLength(_cachedFormHeight);
        else if (FormStackPanel.ActualHeight > 0)
            FormRow.Height = new GridLength(FormStackPanel.ActualHeight + 56);
        else
            FormRow.Height = new GridLength(1, GridUnitType.Star);
    }

    // ---- Replace with confirmation ----
    private async void OnReplaceClicked(object sender, RoutedEventArgs e) => await ConfirmAndReplaceAsync();

    private async Task ConfirmAndReplaceAsync()
    {
        if (!ViewModel.IsReplaceEnabled) return;

        // "Don't warn when replacing" in Settings makes the Replace button behave like Ctrl+Alt+Enter:
        // immediate replace, no backup, no dialog.
        if (_settingsStore.Load().DontWarnWhenReplacing)
        {
            await ViewModel.ReplaceCommand.ExecuteAsync(null);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Confirm replace",
            Content = $"Replace will rewrite {ViewModel.FilteredResults.Count:N0} file(s) on disk."
                    + Environment.NewLine + Environment.NewLine
                    + "• Replace with backups: writes a .bak copy next to each modified file (use Undo to restore)."
                    + Environment.NewLine
                    + "• Replace: overwrites in place. Cannot be undone.",
            PrimaryButtonText = "Replace with backups",
            SecondaryButtonText = "Replace",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await ViewModel.ReplaceWithBackupsCommand.ExecuteAsync(null);
        else if (result == ContentDialogResult.Secondary)
            await ViewModel.ReplaceCommand.ExecuteAsync(null);
        // Close (Cancel) → do nothing.
    }

    // ---- Column sort ----
    private void OnHeaderTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string column }) return;

        if (column == _sortColumn)
        {
            _sortDirection = SortDirectionLogic.ToggleNext(_sortDirection);
            if (_sortDirection == SortDirection.None) _sortColumn = null;
        }
        else
        {
            _sortColumn = column;
            _sortDirection = SortDirection.Ascending;
        }

        ApplySort();
        UpdateHeaderTexts();
    }

    private void ApplySort()
    {
        IOrderedEnumerable<FileMatchViewModel> ordered = _sortColumn switch
        {
            "Name" => OrderBy(x => x.FileName),
            "Size" => OrderBy(x => x.FileLength),
            "Matches" => OrderBy(x => x.MatchCount),
            "Path" => OrderBy(x => x.Directory),
            "Ext" => OrderBy(x => x.Extension),
            "Encoding" => OrderBy(x => x.EncodingName),
            "Date" => OrderBy(x => x.FileLastWriteTime),
            _ => ViewModel.Results.OrderBy(x => x.InsertionIndex),
        };
        // Always apply Name as secondary when primary is Path, so siblings sort alphabetically.
        if (_sortColumn == "Path")
        {
            ordered = _sortDirection == SortDirection.Descending
                ? ordered.ThenByDescending(x => x.FileName)
                : ordered.ThenBy(x => x.FileName);
        }
        ViewModel.ReplaceResults(ordered.ToList());
    }

    private IOrderedEnumerable<FileMatchViewModel> OrderBy<TKey>(Func<FileMatchViewModel, TKey> key) =>
        _sortDirection == SortDirection.Descending
            ? ViewModel.Results.OrderByDescending(key)
            : ViewModel.Results.OrderBy(key);

    private void UpdateHeaderTexts()
    {
        NameHeader.Text = HeaderText("Name", "Name");
        SizeHeader.Text = HeaderText("Size", "Size");
        MatchesHeader.Text = HeaderText("Matches", "Matches");
        PathHeader.Text = HeaderText("Path", "Path");
        ExtHeader.Text = HeaderText("Ext", "Ext");
        EncodingHeader.Text = HeaderText("Encoding", "Encoding");
        DateHeader.Text = HeaderText("Date", "Date modified");
    }

    private string HeaderText(string column, string display) =>
        SortDirectionLogic.FormatHeader(display, column == _sortColumn, _sortDirection);

    private static bool TryParseHotkey(string hotkey, out VirtualKey key, out VirtualKeyModifiers modifiers)
    {
        // Numeric values of HotkeyParser.Modifier and the Win32 VK_* codes are aligned with the WinUI
        // VirtualKeyModifiers / VirtualKey enums by design, so a direct cast is safe and stays in sync
        // with whatever the parser returns.
        if (HotkeyParser.TryParse(hotkey, out var parsed))
        {
            key = (VirtualKey)parsed.VirtualKeyCode;
            modifiers = (VirtualKeyModifiers)parsed.Modifiers;
            return true;
        }
        key = VirtualKey.None;
        modifiers = VirtualKeyModifiers.None;
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

