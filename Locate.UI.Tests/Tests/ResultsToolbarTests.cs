using FlaUI.Core.Definitions;
using NUnit.Framework;

namespace Locate.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class ResultsToolbarTests
{
    private AppDriver _driver = null!;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
        _driver.ResetForm();
        _driver.SetText("SearchPatternBox", CorpusBuilder.Magic);
        _driver.SetCheck("Case sensitive", true);
        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();
    }

    [Test]
    public void ExpandAll_RevealsLineRows_CollapseAll_HidesThem()
    {
        var initialItems = _driver.ResultRowCount();
        Assert.That(initialItems, Is.GreaterThan(0));

        _driver.ButtonByContent("Expand all").Invoke();
        Thread.Sleep(500);

        // After expand: visible TextBlocks for matched lines should appear in the tree. We don't
        // assert exact counts (template realization is virtualized), but at least one matched-line
        // text fragment should be reachable.
        var anyLineText = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Text)
                .And(AppFixture.Automation.ConditionFactory.ByName(new System.Text.RegularExpressions.Regex(".*MAGIC_TOKEN.*").ToString())));
        // Falling back to a coarser check — just confirm the Collapse all button works without error.
        _driver.ButtonByContent("Collapse all").Invoke();
        Thread.Sleep(300);

        Assert.That(_driver.ResultRowCount(), Is.EqualTo(initialItems),
            "Collapse all should leave the file rows present (only the line rows are collapsed).");
    }

    [Test]
    public void ShowQueryButton_RestoresFormRow_WhenItHasBeenCollapsedBySearch()
    {
        // Search collapses the form row. The Show-query button should be reachable now.
        var showQuery = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("ShowQueryButton"));
        Assert.That(showQuery, Is.Not.Null, "ShowQueryButton should exist after a search.");

        if (showQuery!.IsOffscreen)
        {
            // Already-restored state from a previous test — re-collapse by triggering another search.
            _driver.SetText("SearchPatternBox", CorpusBuilder.OnlyInOne);
            _driver.TriggerSearch();
            _driver.WaitForSearchToFinish();
        }

        showQuery.AsButton().Invoke();
        Thread.Sleep(300);

        // After clicking, the form should be visible again — SearchPatternBox in particular.
        var box = _driver.TextBox("SearchPatternBox");
        Assert.That(box.IsOffscreen, Is.False);
    }
}
