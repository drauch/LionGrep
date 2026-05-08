using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Locate.Core;
using Microsoft.UI.Xaml;

namespace Locate.ViewModels;

public partial class FileMatchViewModel : ObservableObject
{
    private readonly FileMatch _model;

    public FileMatchViewModel(FileMatch model)
    {
        _model = model;
        Lines = new ObservableCollection<LineMatchViewModel>(
            model.ContentMatches.Select(m => new LineMatchViewModel(model.Path, m)));
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
