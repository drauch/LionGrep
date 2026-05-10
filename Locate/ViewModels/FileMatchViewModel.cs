using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Locate.Core;
using Locate.Core.Logic;
using Microsoft.UI.Xaml;

namespace Locate.ViewModels;

/// <summary>
/// One slice of a rendered string in the results view, tagged with its highlight semantics.
/// The XAML binds three Visibility props (one per kind) so each segment paints itself with the
/// matching background — no IValueConverter required.
/// </summary>
public sealed class TextSegment
{
    public TextSegment(string text, HighlightKind kind)
    {
        Text = text;
        Kind = kind;
    }

    public string Text { get; }
    public HighlightKind Kind { get; }

    public Visibility EngineMatchVisibility => Kind == HighlightKind.EngineMatch ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FilterMatchVisibility => Kind == HighlightKind.FilterMatch ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlainVisibility => Kind == HighlightKind.None ? Visibility.Visible : Visibility.Collapsed;
}

public partial class FileMatchViewModel : ObservableObject
{
    private readonly FileMatch _model;
    private readonly string? _displayDirectory;

    public FileMatchViewModel(MainViewModel parent, FileMatch model, int insertionIndex, string? displayDirectory = null)
    {
        Parent = parent;
        _model = model;
        InsertionIndex = insertionIndex;
        _displayDirectory = displayDirectory;
        Lines = new ObservableCollection<LineMatchViewModel>(
            model.ContentMatches.Select(m => new LineMatchViewModel(parent, model.Path, m)));
    }

    public MainViewModel Parent { get; }
    public int InsertionIndex { get; }

    public long FileLength
    {
        get
        {
            try { return new FileInfo(_model.Path).Length; }
            catch { return 0; }
        }
    }

    public DateTime FileLastWriteTime
    {
        get
        {
            try { return new FileInfo(_model.Path).LastWriteTime; }
            catch { return DateTime.MinValue; }
        }
    }

    public string Path => _model.Path;
    public string FileName => System.IO.Path.GetFileName(_model.Path);
    public string Directory => _displayDirectory ?? System.IO.Path.GetDirectoryName(_model.Path) ?? "";
    public string Extension => System.IO.Path.GetExtension(_model.Path);
    public string EncodingName => _model.Encoding switch
    {
        null => "—",
        UTF8Encoding u when u.GetPreamble().Length == 0 => "UTF-8",
        UTF8Encoding => "UTF-8 BOM",
        UnicodeEncoding u when u.GetPreamble()[0] == 0xFF => "UTF-16 LE",
        UnicodeEncoding => "UTF-16 BE",
        UTF32Encoding u when u.GetPreamble()[0] == 0xFF => "UTF-32 LE",
        UTF32Encoding => "UTF-32 BE",
        _ => _model.Encoding.WebName,
    };

    public string SizeText
    {
        get
        {
            try
            {
                var bytes = new FileInfo(_model.Path).Length;
                return FormatSize(bytes);
            }
            catch
            {
                return "—";
            }
        }
    }

    public string DateModifiedText
    {
        get
        {
            try
            {
                return new FileInfo(_model.Path).LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "—";
            }
        }
    }

    public int MatchCount => _model.ContentMatches.Count + _model.NameMatches.Count;
    public string MatchCountText => MatchCount.ToString();

    public ObservableCollection<LineMatchViewModel> Lines { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpandedVisibility))]
    private bool _isExpanded;

    public Visibility IsExpandedVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    public bool IsExpandable => Lines.Count > 0;
    public Visibility ChevronVisibility => IsExpandable ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Segments for the file-name cell. Engine matches come from <see cref="FileMatch.NameMatches"/>;
    /// filter matches come from <see cref="MainViewModel.FilterText"/>. Recomputed on every read so a
    /// filter change just needs to fire <c>PropertyChanged</c> for this property.</summary>
    public IReadOnlyList<TextSegment> FileNameSegments => BuildSegments(FileName, GetFileNameEngineRanges());

    /// <summary>Segments for the directory column. Only filter-driven highlights — the engine never
    /// matches the directory portion of a path.</summary>
    public IReadOnlyList<TextSegment> DirectorySegments => BuildSegments(Directory, engineRanges: null);

    /// <summary>Called by <see cref="MainViewModel"/> when the live filter text changes (after the
    /// 250 ms debounce). Re-emits the segment properties so the UI rebinds.</summary>
    public void RaiseFilterHighlightChanged()
    {
        OnPropertyChanged(nameof(FileNameSegments));
        OnPropertyChanged(nameof(DirectorySegments));
        foreach (var line in Lines)
            line.RaiseFilterHighlightChanged();
    }

    private IReadOnlyList<TextSegment> BuildSegments(string text, IReadOnlyList<HighlightRange>? engineRanges)
    {
        var segments = HighlightSegmenter.Build(text, engineRanges, Parent.FilterText);
        return segments.Select(s => new TextSegment(s.Text, s.Kind)).ToArray();
    }

    /// <summary>Translates the engine's <see cref="FileMatch.NameMatches"/> (which are ranges into
    /// the relative path) into ranges against the bare file name. Returns empty if there's nothing
    /// to highlight.</summary>
    private IReadOnlyList<HighlightRange>? GetFileNameEngineRanges()
    {
        if (_model.NameMatches.Count == 0 || string.IsNullOrEmpty(_model.RelativePath))
            return null;

        var fileName = FileName;
        var fileNameStart = _model.RelativePath.Length - fileName.Length;

        var ranges = new List<HighlightRange>();
        foreach (var span in _model.NameMatches)
        {
            var s = span.Column - fileNameStart;
            var e = s + span.Length;
            if (e <= 0 || s >= fileName.Length) continue;
            s = Math.Max(0, s);
            e = Math.Min(fileName.Length, e);
            if (e > s) ranges.Add(new HighlightRange(s, e - s));
        }
        return ranges.Count == 0 ? null : ranges;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }
}

public sealed class LineMatchViewModel : ObservableObject
{
    private readonly MainViewModel _parent;

    public LineMatchViewModel(MainViewModel parent, string filePath, LineMatch model)
    {
        _parent = parent;
        FilePath = filePath;
        LineNumber = model.LineNumber;
        Column = model.Column;
        Length = model.Length;
        LineText = model.LineText;
    }

    public string FilePath { get; }
    public int LineNumber { get; }
    public int Column { get; }
    public int Length { get; }
    public string LineText { get; }

    public string PositionText => $"{LineNumber,4}:{Column,-3}";

    /// <summary>Segments for the matched line. Engine highlight (yellow) is the single
    /// <c>(Column, Length)</c> range from the search result; filter highlight (blue) overrides
    /// that range wherever the live filter text overlaps it.</summary>
    public IReadOnlyList<TextSegment> LineSegments
    {
        get
        {
            HighlightRange[]? engineRanges = null;
            if (Length > 0 && Column < LineText.Length)
            {
                var len = Math.Min(Length, LineText.Length - Column);
                if (len > 0)
                    engineRanges = [new HighlightRange(Column, len)];
            }

            var segments = HighlightSegmenter.Build(LineText, engineRanges, _parent.FilterText);
#pragma warning disable S2365 // Property by design: XAML x:Bind only wires OneWay updates onto properties, not methods.
            return segments.Select(s => new TextSegment(s.Text, s.Kind)).ToArray();
#pragma warning restore S2365
        }
    }

    public void RaiseFilterHighlightChanged() => OnPropertyChanged(nameof(LineSegments));
}
