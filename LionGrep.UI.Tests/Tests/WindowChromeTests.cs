using NUnit.Framework;

namespace LionGrep.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class WindowChromeTests
{
    private AppDriver _driver = null!;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
        _driver.ResetForm();
    }

    [Test]
    public void WindowTitle_ReflectsFirstLineOfSearchIn()
    {
        var distinctRoot = AppFixture.ReadOnlyCorpus;
        _driver.SetText("SearchInBox", distinctRoot + "\r\nC:\\other-root-that-doesnt-exist");
        Thread.Sleep(300);

        var title = AppFixture.MainWindow.Title ?? "";
        Assert.That(title, Does.Contain("LionGrep"));
        Assert.That(title, Does.Contain(distinctRoot),
            "Window title should display the first non-empty line of Search-in.");
    }

    [Test]
    public void StatusLine_ShowsMatchesInFilesFormat_AfterSearch()
    {
        _driver.SetText("SearchPatternBox", CorpusBuilder.Magic);
        _driver.SetCheck("Case sensitive", true);
        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        var summary = _driver.ReadResultsSummary();
        // Format: "{N} matches in {M} files ({E} files searched, {S} skipped)"
        Assert.That(summary, Does.Match(@"\d.*matches in.*\d.*files.*files searched.*skipped"));
    }
}
