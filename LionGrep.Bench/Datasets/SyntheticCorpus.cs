using System.Text;

namespace LionGrep.Bench.Datasets;

/// <summary>
/// Generates a deterministic temp directory of text files for benchmarking. Designed so the same
/// (seed, profile) always produces the exact same corpus on disk — bench runs are reproducible
/// across processes (so we can run ripgrep against it and compare wall-clock time apples-to-apples).
/// </summary>
public static class SyntheticCorpus
{
    public sealed record Profile(int FileCount, int MinFileSize, int MaxFileSize, double NeedleProbability)
    {
        public static readonly Profile CodeRepoLike = new(FileCount: 5_000, MinFileSize: 256, MaxFileSize: 8 * 1024, NeedleProbability: 0.05);
        public static readonly Profile MixedSizes   = new(FileCount: 2_000, MinFileSize: 1 * 1024, MaxFileSize: 256 * 1024, NeedleProbability: 0.10);
        public static readonly Profile FewLargeFiles = new(FileCount: 50,   MinFileSize: 512 * 1024, MaxFileSize: 4 * 1024 * 1024, NeedleProbability: 0.50);
    }

    public static string Build(string label, Profile profile, int seed, string needle)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"liongrep-bench-{label}-{seed}");
        var marker = Path.Combine(dir, ".bench-built");
        if (File.Exists(marker)) return dir;

        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        var rng = new Random(seed);
        var needleBytes = Encoding.UTF8.GetBytes(needle);

        for (var i = 0; i < profile.FileCount; i++)
        {
            var size = rng.Next(profile.MinFileSize, profile.MaxFileSize);
            var content = BuildFile(rng, size, includeNeedle: rng.NextDouble() < profile.NeedleProbability, needleBytes);
            // Spread files across a few subdirectories so the enumerator does some real recursion.
            var sub = Path.Combine(dir, $"d{i % 32:D2}", $"e{(i / 32) % 16:D2}");
            Directory.CreateDirectory(sub);
            File.WriteAllBytes(Path.Combine(sub, $"f{i:D5}.txt"), content);
        }

        File.WriteAllText(marker, $"profile={profile}\nseed={seed}\nneedle={needle}\n");
        return dir;
    }

    private static byte[] BuildFile(Random rng, int approxSize, bool includeNeedle, byte[] needleBytes)
    {
        // Mostly ASCII tokens with occasional digits/punctuation, organized in lines of ~60–120 chars.
        // Cheap and deterministic; resembles source-code line distributions enough for byte-fastpath
        // and regex-prefilter benchmarks to behave like real workloads.
        using var ms = new MemoryStream(approxSize + 256);
        Span<byte> tokenBuf = stackalloc byte[16];
        var written = 0;
        var lineLen = 0;

        while (written < approxSize)
        {
            var tokLen = rng.Next(2, 12);
            for (var i = 0; i < tokLen; i++)
                tokenBuf[i] = (byte)('a' + rng.Next(26));
            ms.Write(tokenBuf[..tokLen]);
            written += tokLen;
            lineLen += tokLen;

            if (lineLen > 80 + rng.Next(40))
            {
                ms.WriteByte((byte)'\n');
                written++;
                lineLen = 0;
            }
            else
            {
                ms.WriteByte((byte)' ');
                written++;
                lineLen++;
            }
        }

        // If this file should contain the needle, splice it onto a random line.
        var bytes = ms.ToArray();
        if (includeNeedle && bytes.Length > needleBytes.Length + 32)
        {
            var spliceAt = rng.Next(0, bytes.Length - needleBytes.Length);
            // Anchor the splice to a non-letter boundary to keep whole-word matching honest.
            while (spliceAt > 0 && IsAsciiLetter(bytes[spliceAt - 1])) spliceAt--;
            Array.Copy(needleBytes, 0, bytes, spliceAt, needleBytes.Length);
        }
        return bytes;
    }

    private static bool IsAsciiLetter(byte b) => (b | 0x20) is >= (byte)'a' and <= (byte)'z';
}
