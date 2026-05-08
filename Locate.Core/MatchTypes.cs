using System.Text;

namespace Locate.Core;

public readonly record struct MatchSpan(int Column, int Length);

public sealed record LineMatch(int LineNumber, int Column, int Length, string LineText);

public sealed record FileMatch(
    string Path,
    Encoding? Encoding,
    IReadOnlyList<LineMatch> ContentMatches,
    IReadOnlyList<MatchSpan> NameMatches);
