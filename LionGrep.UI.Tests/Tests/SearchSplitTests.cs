using FlaUI.Core.Definitions;
using NUnit.Framework;

namespace LionGrep.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class SearchSplitTests
{
    private AppDriver _driver = null!;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
        _driver.ResetForm();
    }

    [Test]
    public void InverseSearch_ReturnsFilesNotContainingPattern()
    {
        _driver.SetText("SearchPatternBox", CorpusBuilder.OnlyInOne);
        _driver.SetCheck("Case sensitive", true);

        // Open the SplitButton flyout and invoke "Inverse search".
        InvokeSplitMenuItem("Inverse search");
        _driver.WaitForSearchToFinish();

        // Inverse → all files except unique.cs (which contains the pattern). Big corpus has many files,
        // so we just assert "more than 1" — the tightest portable assertion across runs.
        Assert.That(_driver.ResultRowCount(), Is.GreaterThan(1));

        // And the matching file must be absent.
        foreach (var row in _driver.ResultsList().Items)
            Assert.That(row.Name ?? "", Does.Not.Contain("unique.cs"));
    }

    [Test]
    public void SearchInCurrentlyFoundFiles_NarrowsToSubsetOfCurrentResults()
    {
        // Phase 1: broad search — find all "class".
        _driver.SetText("SearchPatternBox", "class");
        _driver.SetCheck("Case sensitive", true);
        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();
        var broadCount = _driver.ResultRowCount();
        Assert.That(broadCount, Is.GreaterThan(0));

        // Phase 2: narrow with a more specific pattern, restricted to currently-found files.
        _driver.SetText("SearchPatternBox", "UserService");
        InvokeSplitMenuItem("Search in currently found files");
        _driver.WaitForSearchToFinish();

        var narrowed = _driver.ResultRowCount();
        Assert.That(narrowed, Is.LessThanOrEqualTo(broadCount));
        Assert.That(narrowed, Is.GreaterThan(0));
    }

    private static void InvokeSplitMenuItem(string itemName)
    {
        // Find the SearchSplit's chevron and click to expand its flyout, then invoke the item.
        var split = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("SearchSplit"));
        Assert.That(split, Is.Not.Null, "SearchSplit not found.");

        // SplitButton exposes two interactive parts: the main button and the dropdown chevron.
        // Look for the chevron sub-button by its accessible name; clicking it opens the menu.
        var chevron = split!.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("Open")));
        if (chevron is null)
        {
            // Fall back to clicking the second button child.
            var buttons = split.FindAllChildren(AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button));
            Assert.That(buttons.Length, Is.GreaterThan(1), "SplitButton has no chevron sub-button.");
            buttons[^1].AsButton().Invoke();
        }
        else
        {
            chevron.AsButton().Invoke();
        }
        Thread.Sleep(200);

        var menuItem = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.MenuItem)
                .And(AppFixture.Automation.ConditionFactory.ByName(itemName)));
        Assert.That(menuItem, Is.Not.Null, $"Menu item '{itemName}' not found.");
        menuItem!.AsMenuItem().Invoke();
    }
}
