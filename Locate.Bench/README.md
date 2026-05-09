# Locate.Bench

Engine-level micro-benchmarks for `Locate.Core`, plus a deterministic synthetic-corpus
generator so we can measure ourselves head-to-head against external tools (ripgrep, grep, …)
on the **exact same** dataset and pattern.

## Run the .NET benchmarks

Always run **Release** with no debugger attached. BenchmarkDotNet relaunches each method in a fresh process for accurate measurement.

```pwsh
dotnet run --project Locate.Bench -c Release -- --filter '*'
```

Selectors:

```pwsh
# A single benchmark
dotnet run --project Locate.Bench -c Release -- --filter '*RegexWithPrefilter*'

# Just the literal-search ones
dotnet run --project Locate.Bench -c Release -- --filter '*Literal*'

# List without running
dotnet run --project Locate.Bench -c Release -- --list flat
```

Output lands in `BenchmarkDotNet.Artifacts/` next to the working directory — markdown, html, csv per run.

## Compare to ripgrep on identical input

The corpus generator is **deterministic**: given the same `(profile, seed)` it produces byte-for-byte
identical files on disk. That lets us `time` ripgrep against the same trees.

```pwsh
# 1) Build (or reuse) the corpus, get its absolute path on stdout.
$corpus = (dotnet run --project Locate.Bench -c Release -- prepare code).Trim()

# 2) Time ripgrep on it. The pattern is "blazingNeedle" — the magic needle the corpus generator
#    splices into ~5% of files at a random byte offset.
Measure-Command { rg --no-stats -c blazingNeedle $corpus | Out-Null }

# 3) Time Locate. The corresponding micro-benchmark is `LiteralCaseSensitive`. Note that BDN
#    reports per-iteration times in nanoseconds; multiply by `OperationsPerInvoke = 1` to get
#    wall time per pass over the corpus.
dotnet run --project Locate.Bench -c Release -- --filter '*LiteralCaseSensitive*'
```

### Profiles

| Profile | Files | Size range | Needle in % of files | Best for measuring |
|---|---|---|---|---|
| `code` (default) | 5,000 | 256 B – 8 KB | 5 % | small-file shortcut + parallelism |
| `mixed` | 2,000 | 1 KB – 256 KB | 10 % | mmap path + line decoding |
| `large` | 50 | 512 KB – 4 MB | 50 % | byte-level fast path throughput |

Pick with the second arg to `prepare`: `prepare mixed`, `prepare large`.

### Patterns the corpus is designed to exercise

The default needle is `blazingNeedle` (ASCII, mixed-case-friendly). For the matching benchmarks:

| Benchmark | Pattern | Hits | Engine path |
|---|---|---|---|
| `LiteralCaseSensitive` | `blazingNeedle` | ~5 %/10 %/50 % depending on profile | UTF-8 byte path, no decode |
| `LiteralCaseInsensitive` | `blazingNeedle` | same | UTF-8 byte path with ASCII case-fold |
| `LiteralNonAscii` | `Größe-blazingNeedle` | 0 (intentional) | UTF-8 byte path, exercises non-match cost |
| `RegexWithPrefilter` | `blazingNeedle\w*` | same | regex with `blazingNeedle` extracted as prefilter |
| `RegexWithoutPrefilter` | `\d{3}-\d{4}` | depends on synthetic content | regex without literal prefilter (worst case) |
| `LiteralWholeWord` | `blazingNeedle` (whole word) | same | byte path + boundary check |

## Iterating with a custom workload

The generator caches the corpus by `(profile, seed)` and skips regeneration on subsequent runs.
To force a clean rebuild, delete the cached marker:

```pwsh
Remove-Item -Recurse "$env:TEMP\locate-bench-*"
```

To benchmark on a real-world tree (Linux kernel sources, your own repo, etc.):

```pwsh
# Add a one-off harness — quickest path is to inline a Searcher call in Program.cs and time it.
# BenchmarkDotNet on real trees is awkward because every iteration needs the same OS page cache state.
$pattern = "TODO"
$root    = "C:\src\linux"
1..3 | ForEach-Object {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    rg -c $pattern $root | Out-Null
    "ripgrep #$_ : $($sw.ElapsedMilliseconds) ms"
    $sw.Restart()
    # ... call our engine ...
}
```

## Notes

- BenchmarkDotNet reports allocations per benchmark when `[MemoryDiagnoser]` is on (it is).
  `Allocated` of zero or near-zero on the literal benchmarks is the goal — that's the byte path
  doing its job.
- The first run on a fresh corpus pays the OS cold-cache cost. Run twice if you want hot-cache
  numbers (BDN's `--warmupCount` covers in-process warmup but not the OS page cache).
- For ripgrep comparisons, both tools should hit the same OS page cache state. Run them in the
  same order, twice, and compare the second run.
