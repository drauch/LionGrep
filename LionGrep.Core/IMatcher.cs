namespace LionGrep.Core;

public interface IMatcher
{
    void FindMatches(ReadOnlySpan<char> line, ICollection<MatchSpan> destination);
}
