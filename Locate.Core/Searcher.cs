namespace Locate.Core;

public sealed record SearchRequest(
    IReadOnlyList<string> Roots,
    FileEnumerationOptions Enumeration,
    SearchOptions Search);

public sealed class Searcher
{
    private readonly FileEnumerator _enumerator = new();
    private readonly FileSearcher _fileSearcher = new();

    public IEnumerable<FileMatch> Search(SearchRequest request, CancellationToken ct = default)
    {
        return Search(request, onFileExamined: null, ct);
    }

    public IEnumerable<FileMatch> Search(SearchRequest request, IProgress<int>? onFileExamined, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var matcher = MatcherFactory.Create(request.Search);
        var hasContentPattern = !string.IsNullOrEmpty(request.Search.Pattern);
        var examined = 0;

        foreach (var file in _enumerator.Enumerate(request.Roots, request.Enumeration, ct))
        {
            ct.ThrowIfCancellationRequested();
            examined++;
            onFileExamined?.Report(examined);

            IReadOnlyList<MatchSpan> nameMatches = [];
            if (request.Search.SearchInNames && hasContentPattern)
            {
                var relative = Path.GetRelativePath(file.RootPath, file.FullPath).Replace('\\', '/');
                var hits = new List<MatchSpan>();
                matcher.FindMatches(relative, hits);
                if (hits.Count > 0)
                    nameMatches = hits;
            }

            FileMatch? contentMatch = null;
            if (hasContentPattern)
            {
                try
                {
                    contentMatch = _fileSearcher.Search(file.FullPath, matcher, ct, request.Search.SkipBinaryFiles);
                }
                catch (NotSupportedException)
                {
                    // > 2 GiB; skipped for v1.
                }
                catch (IOException)
                {
                    // File in use, locked, etc.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            if (contentMatch is not null && nameMatches.Count > 0)
                yield return contentMatch with { NameMatches = nameMatches };
            else if (contentMatch is not null)
                yield return contentMatch;
            else if (nameMatches.Count > 0)
                yield return new FileMatch(file.FullPath, Encoding: null, ContentMatches: [], NameMatches: nameMatches);
        }
    }
}
