using System.Collections.Concurrent;

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

        // Combined "skipped" count = files filter-rejected by the enumerator + files skipped by the binary filter.
        // Both feed into the same caller-facing progress. Enumerator side is single-threaded (called inside the
        // FileSystemEnumerable predicate during enumeration); binary side runs from parallel workers, so we use
        // long fields with Interlocked for the binary counter and just publish snapshots from the enumerator hook.
        long enumeratorRejected = 0;
        long binarySkipped = 0;
        var enumeratorRejectedProgress = onFileRejected is null ? null : new SyncProgress<int>(c =>
        {
            Interlocked.Exchange(ref enumeratorRejected, c);
            onFileRejected.Report((int)(c + Interlocked.Read(ref binarySkipped)));
        });

        long examined = 0;

        var enumerable = _enumerator.Enumerate(request.Roots, request.Enumeration, enumeratorRejectedProgress, ct);
        return ParallelStream(
            enumerable,
            file =>
            {
                Interlocked.Increment(ref examined);
                onFileExamined?.Report((int)Interlocked.Read(ref examined));

                var (toYield, wasBinary) = MatchFile(file.FullPath, file.RootPath, request.Search, matcher, hasContentPattern, ct);

                if (wasBinary)
                {
                    var bs = Interlocked.Increment(ref binarySkipped);
                    onFileRejected?.Report((int)(Interlocked.Read(ref enumeratorRejected) + bs));
                }

                if (request.Search.Invert)
                {
                    // Inverse: yield files that were examined but didn't match. Skip binaries — we couldn't tell.
                    return toYield is null && !wasBinary
                        ? new FileMatch(file.FullPath, Encoding: null, ContentMatches: [], NameMatches: [], RelativePath: null)
                        : null;
                }
                return toYield;
            },
            ct);
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
        long examined = 0;
        long binarySkipped = 0;

        return ParallelStream(
            paths.Select(p => new EnumeratedFile(p, RootPath: "")),
            file =>
            {
                Interlocked.Increment(ref examined);
                onFileExamined?.Report((int)Interlocked.Read(ref examined));

                // No "root" available here, so name matches are evaluated against the leaf file name only.
                var (toYield, wasBinary) = MatchFile(file.FullPath, rootPath: null, options, matcher, hasContentPattern, ct);

                if (wasBinary)
                {
                    var bs = Interlocked.Increment(ref binarySkipped);
                    onFileRejected?.Report((int)bs);
                }

                if (options.Invert)
                {
                    return toYield is null && !wasBinary
                        ? new FileMatch(file.FullPath, Encoding: null, ContentMatches: [], NameMatches: [], RelativePath: null)
                        : null;
                }
                return toYield;
            },
            ct);
    }

    /// <summary>
    /// Pumps <paramref name="source"/> through <paramref name="processFile"/> in parallel and streams the
    /// non-null results back as a synchronous enumerable. Producer dispatches to <c>Environment.ProcessorCount</c>
    /// workers; consumer pulls from a bounded queue so unbounded result backlog can't OOM the process.
    /// Cancelling either side propagates to the other via a linked CTS.
    /// </summary>
    private static IEnumerable<FileMatch> ParallelStream(
        IEnumerable<EnumeratedFile> source,
        Func<EnumeratedFile, FileMatch?> processFile,
        CancellationToken ct)
    {
        var queue = new BlockingCollection<FileMatch>(boundedCapacity: 256);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linkedCts.Token;

        var producer = Task.Run(() =>
        {
            try
            {
                Parallel.ForEach(
                    source,
                    new ParallelOptions
                    {
                        CancellationToken = token,
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                    },
                    file =>
                    {
                        var match = processFile(file);
                        if (match is not null)
                        {
                            try { queue.Add(match, token); }
                            catch (InvalidOperationException) { /* queue completed mid-flight */ }
                            catch (OperationCanceledException) { /* consumer stopped */ }
                        }
                    });
            }
            catch (OperationCanceledException) { /* expected on cancel */ }
            catch (AggregateException ex) when (ex.Flatten().InnerExceptions.All(e => e is OperationCanceledException))
            {
                /* expected on cancel */
            }
            finally
            {
                queue.CompleteAdding();
            }
        }, token);

        return DrainQueue(queue, producer, linkedCts);
    }

    private static IEnumerable<FileMatch> DrainQueue(
        BlockingCollection<FileMatch> queue,
        Task producer,
        CancellationTokenSource linkedCts)
    {
        try
        {
            foreach (var match in queue.GetConsumingEnumerable(linkedCts.Token))
                yield return match;
        }
        finally
        {
            // If the consumer stopped early (caller broke out of foreach), tell the producer to give up so
            // it doesn't keep working on a queue nobody's reading.
            linkedCts.Cancel();
            queue.Dispose();
            try { producer.Wait(); } catch { /* observed */ }
            linkedCts.Dispose();
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
