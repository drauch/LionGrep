namespace Locate.Core;

public interface IMatcher
{
    void FindMatches(ReadOnlySpan<char> line, ICollection<MatchSpan> destination);
}
