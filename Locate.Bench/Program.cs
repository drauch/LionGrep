using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Locate.Bench.Benchmarks;
using Locate.Bench.Datasets;

namespace Locate.Bench;

internal static class Program
{
    static int Main(string[] args)
    {
        // `dotnet run -c Release -- prepare <profile>`
        //   builds (or reuses) the synthetic corpus and prints its path so a separate
        //   `rg <pattern> <corpus-path>` run can hit the exact same data set.
        if (args.Length >= 1 && args[0].Equals("prepare", StringComparison.OrdinalIgnoreCase))
        {
            var profileName = args.Length >= 2 ? args[1] : "code";
            var profile = profileName.ToLowerInvariant() switch
            {
                "code"  => SyntheticCorpus.Profile.CodeRepoLike,
                "mixed" => SyntheticCorpus.Profile.MixedSizes,
                "large" => SyntheticCorpus.Profile.FewLargeFiles,
                _ => throw new ArgumentException($"Unknown profile '{profileName}'. Use code|mixed|large.", nameof(args))
            };
            var dir = SyntheticCorpus.Build(profileName, profile, seed: 42, needle: "blazingNeedle");
            Console.WriteLine(dir);
            return 0;
        }

        // Default: run the BenchmarkDotNet suite.
        var summary = BenchmarkRunner.Run<SearchBenchmarks>(DefaultConfig.Instance, args);
        return summary.HasCriticalValidationErrors ? 1 : 0;
    }
}
