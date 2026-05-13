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
            Assert.That(row.TryGetName() ?? "", Does.Not.Contain("unique.cs"));
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
        // Open the SplitButton's flyout. WinUI 3's SplitButton exposes the ExpandCollapse pattern
        // for its dropdown half, which is the most reliable trigger across SDK builds. Falls back
        // to keyboard (Focus + Alt+Down) if the pattern isn't surfaced for some reason.
        var split = WaitHelpers.WaitFor(
            () => AppFixture.MainWindow.FindFirstDescendant(
                AppFixture.Automation.ConditionFactory.ByAutomationId("SearchSplit")),
            description: "SearchSplit");

        var ec = split.Patterns.ExpandCollapse;
        if (ec.IsSupported)
        {
            ec.Pattern.Expand();
        }
        else
        {
            split.Focus();
            FlaUI.Core.Input.Keyboard.TypeSimultaneously(
                FlaUI.Core.WindowsAPI.VirtualKeyShort.LMENU,
                FlaUI.Core.WindowsAPI.VirtualKeyShort.DOWN);
        }
        var menuItem = WaitHelpers.WaitFor(
            () => AppFixture.MainWindow.FindFirstDescendant(
                AppFixture.Automation.ConditionFactory.ByControlType(ControlType.MenuItem)
                    .And(AppFixture.Automation.ConditionFactory.ByName(itemName))),
            description: $"menu item '{itemName}'");
        menuItem.AsMenuItem().Invoke();
    }
}
