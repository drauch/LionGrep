using System.Text;

namespace LionGrep.Core;

internal interface ILineReplacer
{
    /// <summary>Appends the line with all replacements applied to <paramref name="output"/>. Returns the number of replacements made on this line.</summary>
    int ReplaceLine(ReadOnlySpan<char> line, StringBuilder output);
}
