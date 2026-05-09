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
        => Search(request, onFileExamined: null, onFileRejected: null, ct);

    public IEnumerable<FileMatch> Search(SearchRequest request, IProgress<int>? onFileExamined, CancellationToken ct = default)
        => Search(request, onFileExamined, onFileRejected: null, ct);

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

            var (toYield, wasBinary) = MatchFile(file.FullPath, file.RootPath, request.Search, matcher, hasContentPattern, ct);

            if (wasBinary)
            {
                binarySkipped++;
                onFileRejected?.Report(enumeratorRejected + binarySkipped);
            }

            if (request.Search.Invert)
            {
                // Inverse: yield files that were examined but didn't match. Skip binaries — we couldn't tell.
                if (toYield is null && !wasBinary)
                    yield return new FileMatch(file.FullPath, Encoding: null, ContentMatches: [], NameMatches: [], RelativePath: null);
            }
            else if (toYield is not null)
            {
                yield return toYield;
            }
        }
    }

    /// <summary>Searches an explicit set of file paths (no enumeration, no filters). Used by "Search in currently found files".</summary>
    public IEnumerable<FileMatch> SearchFiles(
        IEnumerable<string> paths,
        SearchOptions options,
        IProgress<int>? onFileExamined = null,
        IProgress<int>? onFileRejected = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);

        var matcher = MatcherFactory.Create(options);
        var hasContentPattern = !string.IsNullOrEmpty(options.Pattern);
        var examined = 0;
        var binarySkipped = 0;

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            examined++;
            onFileExamined?.Report(examined);

            // No "root" available here, so name matches are evaluated against the leaf file name only.
            var (toYield, wasBinary) = MatchFile(path, rootPath: null, options, matcher, hasContentPattern, ct);

            if (wasBinary)
            {
                binarySkipped++;
                onFileRejected?.Report(binarySkipped);
            }

            if (options.Invert)
            {
                if (toYield is null && !wasBinary)
                    yield return new FileMatch(path, Encoding: null, ContentMatches: [], NameMatches: [], RelativePath: null);
            }
            else if (toYield is not null)
            {
                yield return toYield;
            }
        }
    }

    private (FileMatch? Match, bool WasBinary) MatchFile(
        string fullPath, string? rootPath, SearchOptions options, IMatcher matcher, bool hasContentPattern, CancellationToken ct)
    {
        IReadOnlyList<MatchSpan> nameMatches = [];
        string? relativePath = null;
        if (options.SearchInNames && hasContentPattern)
        {
            string nameSource;
            if (rootPath is not null)
            {
                nameSource = Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');
                relativePath = nameSource;
            }
            else
            {
                nameSource = Path.GetFileName(fullPath);
            }

            var hits = new List<MatchSpan>();
            matcher.FindMatches(nameSource, hits);
            if (hits.Count > 0)
                nameMatches = hits;
            else
                relativePath = null;
        }

        FileMatch? contentMatch = null;
        var wasBinary = false;
        if (hasContentPattern)
        {
            try
            {
                contentMatch = _fileSearcher.Search(
                    fullPath, matcher, options.SkipBinaryFiles, options.DotMatchesNewline, out wasBinary, ct);
            }
            catch (NotSupportedException) { /* > 2 GiB; skipped for v1. */ }
            catch (IOException) { /* file in use, locked, etc. */ }
            catch (UnauthorizedAccessException) { }
        }

        FileMatch? toYield;
        if (contentMatch is not null && nameMatches.Count > 0)
            toYield = contentMatch with { NameMatches = nameMatches, RelativePath = relativePath };
        else if (contentMatch is not null)
            toYield = contentMatch;
        else if (nameMatches.Count > 0)
            toYield = new FileMatch(fullPath, Encoding: null, ContentMatches: [], NameMatches: nameMatches, RelativePath: relativePath);
        else
            toYield = null;

        return (toYield, wasBinary);
    }
}
