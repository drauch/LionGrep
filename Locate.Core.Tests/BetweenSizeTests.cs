using NUnit.Framework;

namespace Locate.Core.Tests;

public class BetweenSizeFilterTests
{
    private string _root = string.Empty;
    private FileEnumerator _enumerator = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "locate-betsize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _enumerator = new FileEnumerator();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void BetweenSize_KeepsOnlyInRange()
    {
        File.WriteAllText(Path.Combine(_root, "tiny.txt"), new string('a', 50));
        File.WriteAllText(Path.Combine(_root, "mid.txt"), new string('a', 500));
        File.WriteAllText(Path.Combine(_root, "big.txt"), new string('a', 5000));

        var files = _enumerator.Enumerate([_root], new FileEnumerationOptions
        {
            Size = new SizeFilter(SizeFilterMode.Between, 100, 1000),
        }).Select(f => Path.GetFileName(f.FullPath)).Order().ToList();

        Assert.That(files, Is.EqualTo(new[] { "mid.txt" }));
    }
}
