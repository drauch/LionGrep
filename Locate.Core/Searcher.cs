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
        return Search(request, onFileExamined: null, onFileRejected: null, ct);
    }

    public IEnumerable<FileMatch> Search(SearchRequest request, IProgress<int>? onFileExamined, CancellationToken ct = default)
    {
        return Search(request, onFileExamined, onFileRejected: null, ct);
    }

    public IEnumerable<FileMatch> Search(SearchRequest request, IProgress<int>? onFileExamined, IProgress<int>? onFileRejected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var matcher = MatcherFactory.Create(request.Search);
        var hasContentPattern = !string.IsNullOrEmpty(request.Search.Pattern);
        var examined = 0;

        // Combined "skipped" count = files filter-rejected by the enumerator + files skipped by the binary filter.
        var enumeratorRejected = 0;
        var binarySkipped = 0;
        var enumeratorRejectedProgress = onFileRejected is null ? null : new SyncProgress<int>(c =>
        {
            enumeratorRejected = c;
            onFileRejected.Report(enumeratorRejected + binarySkipped);
        });

        foreach (var file in _enumerator.Enumerate(request.Roots, request.Enumeration, enumeratorRejectedProgress, ct))
        {
            ct.ThrowIfCancellationRequested();
            examined++;
            onFileExamined?.Report(examined);

            IReadOnlyList<MatchSpan> nameMatches = [];
            string? relativePath = null;
            if (request.Search.SearchInNames && hasContentPattern)
            {
                var relative = Path.GetRelativePath(file.RootPath, file.FullPath).Replace('\\', '/');
                var hits = new List<MatchSpan>();
                matcher.FindMatches(relative, hits);
                if (hits.Count > 0)
                {
                    nameMatches = hits;
                    relativePath = relative;
                }
            }

            FileMatch? contentMatch = null;
            var wasBinary = false;
            if (hasContentPattern)
            {
                try
                {
                    contentMatch = _fileSearcher.Search(file.FullPath, matcher, request.Search.SkipBinaryFiles, out wasBinary, ct);
                }
                catch (NotSupportedException) { /* > 2 GiB; skipped for v1. */ }
                catch (IOException) { /* File in use, locked, etc. */ }
                catch (UnauthorizedAccessException) { }
            }

            if (wasBinary)
            {
                binarySkipped++;
                onFileRejected?.Report(enumeratorRejected + binarySkipped);
            }

            if (contentMatch is not null && nameMatches.Count > 0)
                yield return contentMatch with { NameMatches = nameMatches, RelativePath = relativePath };
            else if (contentMatch is not null)
                yield return contentMatch;
            else if (nameMatches.Count > 0)
                yield return new FileMatch(file.FullPath, Encoding: null, ContentMatches: [], NameMatches: nameMatches, RelativePath: relativePath);
        }
    }
}
