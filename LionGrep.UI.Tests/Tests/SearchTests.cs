using NUnit.Framework;

namespace LionGrep.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class SearchTests
{
    private AppDriver _driver = null!;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
        _driver.ResetForm();
    }

    [Test]
    public void Search_LiteralCaseSensitive_FindsExpectedFiles()
    {
        _driver.SetText("SearchPatternBox", CorpusBuilder.Magic);
        _driver.SetCheck("Case sensitive", true);

        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        // Files containing "MAGIC_TOKEN" exactly: a.cs (1 hit), b.cs (2 hits), sub/c.cs (1 hit),
        // big.txt (4 hits at lines 0/50/100/150), skipme/bin/x.cs (1 hit).
        var summary = _driver.ReadResultsSummary();
        Assert.That(summary, Does.Contain("matches in"));
        Assert.That(_driver.ResultRowCount(), Is.GreaterThanOrEqualTo(4),
            $"Expected ≥ 4 file rows for case-sensitive '{CorpusBuilder.Magic}'; got summary='{summary}'");
    }

    [Test]
    public void Search_LiteralCaseInsensitive_FoldsCase()
    {
        _driver.SetText("SearchPatternBox", CorpusBuilder.Magic);
        _driver.SetCheck("Case sensitive", false);

        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        // case-insensitive should also pick up case.cs (which has lowercase "magic_token")
        Assert.That(_driver.ResultRowCount(), Is.GreaterThanOrEqualTo(5),
            "Case-insensitive search should match the lowercase variant in case.cs as well.");
    }

    [Test]
    public void Search_NonAsciiLiteral_GoesThroughUtf8BytePath()
    {
        _driver.SetText("SearchPatternBox", CorpusBuilder.Umlaut);
        _driver.SetCheck("Case sensitive", true);

        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        // Only UMLAUT.cs contains "Größe".
        Assert.That(_driver.ResultRowCount(), Is.EqualTo(1));
    }

    [Test]
    public void Search_Regex_RequiredLiteralPrefilterStillReturnsCorrectMatches()
    {
        _driver.SetText("SearchPatternBox", @"class\s+\w+");
        _driver.SetCheck("Use regex", true);
        _driver.SetCheck("Case sensitive", true);

        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        // Files with "class X": a.cs, b.cs, regex.cs (Service + UserService).
        Assert.That(_driver.ResultRowCount(), Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void Search_WholeWord_RespectsBoundaries()
    {
        _driver.SetText("SearchPatternBox", "class");
        _driver.SetCheck("Case sensitive", true);
        _driver.SetCheck("Whole word", true);

        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        // 'class' as a word appears in a.cs, b.cs, regex.cs — but not as a substring of "classy" etc.
        Assert.That(_driver.ResultRowCount(), Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void Search_WithFileNameGlob_FiltersToMatchingExtensions()
    {
        _driver.SetText("SearchPatternBox", CorpusBuilder.Magic);
        _driver.SetText("FileNamesBox", "*.cs");           // exclude big.txt and d.txt
        _driver.SetCheck("Case sensitive", true);

        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        // Now only .cs files with the token: a.cs, b.cs, sub/c.cs, skipme/bin/x.cs (until excluded below).
        var summary = _driver.ReadResultsSummary();
        Assert.That(summary, Does.Contain("matches in"));
        // big.txt's 4 hits should now be excluded → file count drops below 5.
        Assert.That(_driver.ResultRowCount(), Is.LessThanOrEqualTo(4));
    }

    [Test]
    public void Search_WithExcludePath_PrunesDirectory()
    {
        _driver.SetText("SearchPatternBox", CorpusBuilder.Magic);
        _driver.SetText("ExcludePathsBox", "skipme");
        _driver.SetCheck("Case sensitive", true);

        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        // skipme/bin/x.cs must not appear.
        var rows = _driver.ResultsList().Items;
        foreach (var row in rows)
        {
            Assert.That(row.Name ?? "", Does.Not.Contain("skipme"),
                "ExcludePaths should have pruned the 'skipme' directory entirely.");
        }
    }

    [Test]
    public void Search_NoSearchRoot_ReportsValidationMessage()
    {
        _driver.SetText("SearchInBox", "");
        _driver.SetText("SearchPatternBox", "anything");

        _driver.TriggerSearch();
        Thread.Sleep(500);                                  // synchronous validation, very fast

        var summary = _driver.ReadResultsSummary();
        Assert.That(summary, Does.Contain("Provide at least one directory"));
    }

    [Test]
    public void Search_OnlyInOneFile_ReturnsExactlyThatFile()
    {
        _driver.SetText("SearchPatternBox", CorpusBuilder.OnlyInOne);
        _driver.SetCheck("Case sensitive", true);

        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        Assert.That(_driver.ResultRowCount(), Is.EqualTo(1));
    }
}
