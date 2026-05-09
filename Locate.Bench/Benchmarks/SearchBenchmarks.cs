using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Order;
using Locate.Bench.Datasets;
using Locate.Core;

namespace Locate.Bench.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SearchBenchmarks
{
    private const string Needle = "blazingNeedle";

    private string _corpus = string.Empty;
    private Searcher _searcher = null!;

    [GlobalSetup]
    public void Setup()
    {
        _corpus = SyntheticCorpus.Build("code", SyntheticCorpus.Profile.CodeRepoLike, seed: 42, Needle);
        _searcher = new Searcher();
    }

    [Benchmark(Description = "Literal, case-sensitive ASCII (byte fast path)")]
    public int LiteralCaseSensitive() => CountResults(new SearchOptions
    {
        Pattern = Needle,
        CaseSensitive = true,
    });

    [Benchmark(Description = "Literal, case-insensitive ASCII (byte fast path)")]
    public int LiteralCaseInsensitive() => CountResults(new SearchOptions
    {
        Pattern = Needle,
        CaseSensitive = false,
    });

    [Benchmark(Description = "Literal, case-sensitive non-ASCII (UTF-8 byte path)")]
    public int LiteralNonAscii() => CountResults(new SearchOptions
    {
        Pattern = "Größe-" + Needle,    // non-ASCII forces the UTF-8 byte path
        CaseSensitive = true,
    });

    [Benchmark(Description = "Regex with literal prefilter (\"blazingNeedle\\\\w*\")")]
    public int RegexWithPrefilter() => CountResults(new SearchOptions
    {
        Pattern = $@"{Needle}\w*",
        UseRegex = true,
        CaseSensitive = true,
    });

    [Benchmark(Description = "Regex without literal prefilter (\\\\d{3}-\\\\d{4})")]
    public int RegexWithoutPrefilter() => CountResults(new SearchOptions
    {
        Pattern = @"\d{3}-\d{4}",
        UseRegex = true,
        CaseSensitive = true,
    });

    [Benchmark(Description = "Whole-word literal (byte fast path + boundary check)")]
    public int LiteralWholeWord() => CountResults(new SearchOptions
    {
        Pattern = Needle,
        CaseSensitive = true,
        WholeWord = true,
    });

    private int CountResults(SearchOptions search)
    {
        var request = new SearchRequest(
            Roots: [_corpus],
            Enumeration: new FileEnumerationOptions(),
            Search: search);

        var n = 0;
        foreach (var match in _searcher.Search(request))
            n += match.ContentMatches.Count + match.NameMatches.Count;
        return n;
    }
}
