using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Locate.Core;
using Microsoft.UI.Xaml;

namespace Locate.ViewModels;

public sealed class FileNameSegment
{
    public FileNameSegment(string text, bool isMatched)
    {
        Text = text;
        IsMatched = isMatched;
    }

    public string Text { get; }
    public bool IsMatched { get; }

    public Visibility MatchedVisibility => IsMatched ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UnmatchedVisibility => IsMatched ? Visibility.Collapsed : Visibility.Visible;
}

public partial class FileMatchViewModel : ObservableObject
{
    private readonly FileMatch _model;

    public FileMatchViewModel(MainViewModel parent, FileMatch model, int insertionIndex)
    {
        Parent = parent;
        _model = model;
        InsertionIndex = insertionIndex;
        Lines = new ObservableCollection<LineMatchViewModel>(
            model.ContentMatches.Select(m => new LineMatchViewModel(model.Path, m)));
        FileNameSegments = ComputeFileNameSegments(model);
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
    public string Directory => System.IO.Path.GetDirectoryName(_model.Path) ?? "";
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

    public IReadOnlyList<FileNameSegment> FileNameSegments { get; }

    private static IReadOnlyList<FileNameSegment> ComputeFileNameSegments(FileMatch model)
    {
        var fileName = System.IO.Path.GetFileName(model.Path);
        if (model.NameMatches.Count == 0 || string.IsNullOrEmpty(model.RelativePath))
            return [new FileNameSegment(fileName, false)];

        var fileNameStart = model.RelativePath.Length - fileName.Length;

        // Translate, clamp, and merge match spans into the file-name segment of the relative path.
        var matched = new List<(int Start, int End)>();
        foreach (var span in model.NameMatches)
        {
            var s = span.Column - fileNameStart;
            var e = s + span.Length;
            if (e <= 0 || s >= fileName.Length) continue;
            s = Math.Max(0, s);
            e = Math.Min(fileName.Length, e);
            if (e > s) matched.Add((s, e));
        }
        if (matched.Count == 0)
            return [new FileNameSegment(fileName, false)];

        // Merge overlapping/adjacent ranges.
        matched.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(int Start, int End)> { matched[0] };
        for (var i = 1; i < matched.Count; i++)
        {
            var prev = merged[^1];
            var curr = matched[i];
            if (curr.Start <= prev.End)
                merged[^1] = (prev.Start, Math.Max(prev.End, curr.End));
            else
                merged.Add(curr);
        }

        var segments = new List<FileNameSegment>();
        var cursor = 0;
        foreach (var (s, e) in merged)
        {
            if (s > cursor) segments.Add(new FileNameSegment(fileName[cursor..s], false));
            segments.Add(new FileNameSegment(fileName[s..e], true));
            cursor = e;
        }
        if (cursor < fileName.Length)
            segments.Add(new FileNameSegment(fileName[cursor..], false));

        return segments;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }
}

public sealed class LineMatchViewModel
{
    public LineMatchViewModel(string filePath, LineMatch model)
    {
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
    public string PrefixText => LineText[..Math.Min(Column, LineText.Length)];
    public string MatchText => Length > 0 && Column < LineText.Length
        ? LineText.Substring(Column, Math.Min(Length, LineText.Length - Column))
        : "";
    public string SuffixText => Column + Length < LineText.Length
        ? LineText[(Column + Length)..]
        : "";
}
