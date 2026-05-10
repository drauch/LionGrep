using System.Text;

namespace LionGrep.Core.Logic;

/// <summary>
/// Builds RFC 4180 quoted CSV (<c>Name,Path,Line,Column,Text</c>) from a list of file/line records.
/// Pulled out of the WinUI code-behind so it can be unit-tested independently and reused by both
/// "Copy as CSV" and "Export to CSV". The input is plain DTOs to keep the helper free of any
/// FrameworkElement / ViewModel dependency.
/// </summary>
public static class CsvBuilder
{
    public sealed record FileEntry(string FileName, string Path, IReadOnlyList<LineEntry> Lines);
    public sealed record LineEntry(int LineNumber, int Column, string Text);

    public static string Build(IEnumerable<FileEntry> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Path,Line,Column,Text");
        foreach (var file in files)
        {
            if (file.Lines.Count == 0)
            {
                sb.Append(Escape(file.FileName)).Append(',')
                  .Append(Escape(file.Path)).Append(',')
                  .Append(',').Append(',')
                  .AppendLine();
                continue;
            }
            foreach (var line in file.Lines)
            {
                sb.Append(Escape(file.FileName)).Append(',')
                  .Append(Escape(file.Path)).Append(',')
                  .Append(line.LineNumber).Append(',')
                  .Append(line.Column).Append(',')
                  .Append(Escape(line.Text))
                  .AppendLine();
            }
        }
        return sb.ToString();
    }

    /// <summary>RFC 4180 escaping: wrap in <c>"…"</c> if the value contains a comma, quote, CR, or LF;
    /// double internal quotes inside the wrapper. Unchanged otherwise.</summary>
    public static string Escape(string? value)
    {
        if (value is null) return "";
        var needsQuotes = value.AsSpan().IndexOfAny(",\"\r\n".AsSpan()) >= 0;
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
