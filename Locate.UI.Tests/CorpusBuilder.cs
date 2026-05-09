using System.Text;

namespace Locate.UI.Tests;

/// <summary>
/// Builds deterministic temp directories of test files. The "read-only" corpus is created once per
/// test run and shared across all read-only search tests; mutating tests (Replace, Delete) call
/// <see cref="CreateIsolated"/> to get their own throwaway tree.
/// </summary>
internal static class CorpusBuilder
{
    public const string Magic = "MAGIC_TOKEN";          // appears in some files for assertion-keying
    public const string MagicCase = "magic_token";       // case-insensitive variant
    public const string Umlaut = "Größe";                // exercises non-ASCII byte path
    public const string OnlyInOne = "UniqueOneOf999";    // appears in exactly one file

    public static string BuildReadOnlyCorpus()
    {
        var dir = Path.Combine(Path.GetTempPath(), "locate-uitests-ro");
        var marker = Path.Combine(dir, ".built");
        if (File.Exists(marker)) return dir;

        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        // A handful of files with distinct contents so individual tests can pin specific match counts.
        Write(dir, "src/a.cs",      $"// header line\nclass Foo {{ }}\n// {Magic} on line 3\n");
        Write(dir, "src/b.cs",      $"// b.cs\nclass Bar {{ }}\n{Magic}\n{Magic}\n");
        Write(dir, "src/sub/c.cs",  $"interface IThing {{}}\n// uppercase {Magic}\n");
        Write(dir, "src/d.txt",     $"plain text without the token here\n");
        Write(dir, "src/UMLAUT.cs", $"var name = \"{Umlaut}\";\nint len = {Umlaut}.Length;\n");
        Write(dir, "src/case.cs",   $"// {MagicCase} only\n");
        Write(dir, "src/unique.cs", $"// {OnlyInOne}\n");
        Write(dir, "src/regex.cs",  $"public class Service\nclass UserService\nclass Other\n");
        Write(dir, "src/big.txt",   string.Join('\n', Enumerable.Range(0, 200).Select(i => $"line {i:D3}: filler tokens here {(i % 50 == 0 ? Magic : "")}")));
        Write(dir, "skipme/bin/x.cs", $"// would-match {Magic} but lives in bin/\n");

        File.WriteAllText(marker, "ok");
        return dir;
    }

    public static string CreateIsolated(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"locate-uitests-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void DeleteIfExists(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }

    private static void Write(string root, string relativePath, string content)
    {
        var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
