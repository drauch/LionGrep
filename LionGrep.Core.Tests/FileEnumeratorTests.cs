using NUnit.Framework;

namespace LionGrep.Core.Tests;

public class FileEnumeratorTests
{
    private string _root = string.Empty;
    private FileEnumerator _enumerator = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "liongrep-enum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _enumerator = new FileEnumerator();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Touch(string relative, string content = "")
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private List<string> Run(FileEnumerationOptions options) =>
        [.. _enumerator.Enumerate([_root], options).Select(f => Path.GetRelativePath(_root, f.FullPath).Replace('\\', '/')).Order()];

    [Test]
    public void NoFilters_ReturnsAllFilesRecursively()
    {
        Touch("a.txt");
        Touch("sub/b.txt");
        Touch("sub/deep/c.txt");

        var files = Run(new FileEnumerationOptions());

        Assert.That(files, Is.EqualTo(new[] { "a.txt", "sub/b.txt", "sub/deep/c.txt" }));
    }

    [Test]
    public void IncludeSubfoldersOff_StaysAtRoot()
    {
        Touch("a.txt");
        Touch("sub/b.txt");

        var files = Run(new FileEnumerationOptions { IncludeSubfolders = false });

        Assert.That(files, Is.EqualTo(new[] { "a.txt" }));
    }

    [Test]
    public void GlobInclude_MatchesByExtensionAtAnyDepth()
    {
        Touch("a.cs");
        Touch("a.txt");
        Touch("sub/b.cs");
        Touch("sub/b.txt");

        var files = Run(new FileEnumerationOptions { FileNamePatterns = "*.cs" });

        Assert.That(files, Is.EqualTo(new[] { "a.cs", "sub/b.cs" }));
    }

    [Test]
    public void GlobMultipleIncludesPipeSeparated()
    {
        Touch("a.cs");
        Touch("a.ts");
        Touch("a.txt");

        var files = Run(new FileEnumerationOptions { FileNamePatterns = "*.cs|*.ts" });

        Assert.That(files, Is.EqualTo(new[] { "a.cs", "a.ts" }));
    }

    [Test]
    public void GlobBangPrefixExcludes()
    {
        Touch("a.cs");
        Touch("a.Generated.cs");
        Touch("b.cs");

        var files = Run(new FileEnumerationOptions { FileNamePatterns = "*.cs|!*.Generated.cs" });

        Assert.That(files, Is.EqualTo(new[] { "a.cs", "b.cs" }));
    }

    [Test]
    public void RegexFileFilter_MatchesAgainstRelativePath()
    {
        Touch("a.cs");
        Touch("a.txt");
        Touch("sub/b.cs");

        var files = Run(new FileEnumerationOptions
        {
            FileNamePatterns = @"\.cs$",
            FileNamePatternMode = PatternMode.Regex,
        });

        Assert.That(files, Is.EqualTo(new[] { "a.cs", "sub/b.cs" }));
    }

    [Test]
    public void GlobExcludePath_SkipsDirectoriesByLeafName()
    {
        Touch("src/a.cs");
        Touch("bin/b.cs");
        Touch("obj/c.cs");
        Touch("sub/bin/d.cs");

        var files = Run(new FileEnumerationOptions { ExcludePathPatterns = "bin|obj" });

        Assert.That(files, Is.EqualTo(new[] { "src/a.cs" }));
    }

    [Test]
    public void RegexExcludePath_MatchesAgainstRelativePath()
    {
        Touch("a.cs");
        Touch("tests/fixtures/b.cs");
        Touch("tests/unit/c.cs");

        var files = Run(new FileEnumerationOptions
        {
            ExcludePathPatterns = @"^tests/fixtures$",
            ExcludePathPatternMode = PatternMode.Regex,
        });

        Assert.That(files, Is.EqualTo(new[] { "a.cs", "tests/unit/c.cs" }));
    }

    [Test]
    public void GlobExcludePath_AlsoExcludesFilesByName()
    {
        Touch("a.cs");
        Touch("a.tmp");
        Touch("sub/b.tmp");

        var files = Run(new FileEnumerationOptions { ExcludePathPatterns = "*.tmp" });

        Assert.That(files, Is.EqualTo(new[] { "a.cs" }));
    }

    [Test]
    public void GlobExcludePath_PathStyleTokenExcludesNestedFiles()
    {
        Touch("src/a.cs");
        Touch("tests/fixtures/data/b.cs");
        Touch("tests/unit/c.cs");

        var files = Run(new FileEnumerationOptions { ExcludePathPatterns = "tests/fixtures" });

        Assert.That(files, Is.EqualTo(new[] { "src/a.cs", "tests/unit/c.cs" }));
    }

    [Test]
    public void SizeFilter_LessThan_KeepsSmallFiles()
    {
        Touch("small.txt", new string('a', 50));
        Touch("big.txt", new string('a', 5000));

        var files = Run(new FileEnumerationOptions { Size = new SizeFilter(SizeFilterMode.LessThan, 100) });

        Assert.That(files, Is.EqualTo(new[] { "small.txt" }));
    }

    [Test]
    public void SizeFilter_GreaterThan_KeepsLargeFiles()
    {
        Touch("small.txt", new string('a', 50));
        Touch("big.txt", new string('a', 5000));

        var files = Run(new FileEnumerationOptions { Size = new SizeFilter(SizeFilterMode.GreaterThan, 100) });

        Assert.That(files, Is.EqualTo(new[] { "big.txt" }));
    }

    [Test]
    public void DateFilter_NewerThan_KeepsRecent()
    {
        var oldFile = Touch("old.txt");
        var newFile = Touch("new.txt");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-10));
        File.SetLastWriteTime(newFile, DateTime.Now);

        var files = Run(new FileEnumerationOptions
        {
            Date = new DateFilter(DateFilterMode.NewerThan, DateTime.Now.AddDays(-1)),
        });

        Assert.That(files, Is.EqualTo(new[] { "new.txt" }));
    }

    [Test]
    public void DateFilter_OlderThan_KeepsAged()
    {
        var oldFile = Touch("old.txt");
        var newFile = Touch("new.txt");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-10));
        File.SetLastWriteTime(newFile, DateTime.Now);

        var files = Run(new FileEnumerationOptions
        {
            Date = new DateFilter(DateFilterMode.OlderThan, DateTime.Now.AddDays(-1)),
        });

        Assert.That(files, Is.EqualTo(new[] { "old.txt" }));
    }

    [Test]
    public void DateFilter_Between_KeepsInRange()
    {
        var fileA = Touch("a.txt");
        var fileB = Touch("b.txt");
        var fileC = Touch("c.txt");
        File.SetLastWriteTime(fileA, DateTime.Now.AddDays(-10));
        File.SetLastWriteTime(fileB, DateTime.Now.AddDays(-5));
        File.SetLastWriteTime(fileC, DateTime.Now);

        var files = Run(new FileEnumerationOptions
        {
            Date = new DateFilter(DateFilterMode.Between, DateTime.Now.AddDays(-7), DateTime.Now.AddDays(-3)),
        });

        Assert.That(files, Is.EqualTo(new[] { "b.txt" }));
    }

    [Test]
    public void HiddenFiles_ExcludedByDefault()
    {
        Touch("v.txt");
        var hidden = Touch("h.txt");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

        var files = Run(new FileEnumerationOptions());

        Assert.That(files, Is.EqualTo(new[] { "v.txt" }));
    }

    [Test]
    public void HiddenFiles_IncludedWhenToggled()
    {
        Touch("v.txt");
        var hidden = Touch("h.txt");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

        var files = Run(new FileEnumerationOptions { IncludeHidden = true });

        Assert.That(files, Is.EqualTo(new[] { "h.txt", "v.txt" }));
    }

    [Test]
    public void NonExistentRoot_IsSkipped()
    {
        Touch("a.txt");
        var bogus = Path.Combine(Path.GetTempPath(), "definitely-not-a-real-dir-" + Guid.NewGuid());

        var files = _enumerator
            .Enumerate([bogus, _root], new FileEnumerationOptions())
            .Select(f => Path.GetRelativePath(_root, f.FullPath).Replace('\\', '/'))
            .Order()
            .ToList();

        Assert.That(files, Is.EqualTo(new[] { "a.txt" }));
    }
}
