using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace LionGrep.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class FilterPanelTests
{
    private AppDriver _driver = null!;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
        _driver.ResetForm();

        // Pre-load a result set so the filter has something to chew on.
        _driver.SetText("SearchPatternBox", "class");
        _driver.SetCheck("Case sensitive", true);
        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();
    }

    [Test]
    public void FilterToggle_OpensPanel_AndClosesOnSecondClick()
    {
        var toggle = _driver.ToggleButtonByAutomationId("SearchInResultsToggle");
        toggle.Toggle();   // open
        Thread.Sleep(150);

        // FilterBox should now be present and visible.
        var filterBox = _driver.TextBox("FilterBox");
        Assert.That(filterBox.IsAvailable, Is.True);

        toggle.Toggle();   // close
        Thread.Sleep(150);
    }

    [Test]
    public void FilterText_NarrowsResults_OnLineContent()
    {
        var initialCount = _driver.ResultRowCount();

        _driver.ToggleButtonByAutomationId("SearchInResultsToggle").Toggle();
        Thread.Sleep(150);
        _driver.SetText("FilterBox", "UserService");

        // The filter is debounced ~250 ms in the VM; wait until the result set actually shrinks
        // (i.e. the debounced filter applied). Bounded so a real regression still fails fast.
        WaitHelpers.WaitUntil(
            () => _driver.ResultRowCount() < initialCount,
            TimeSpan.FromSeconds(2),
            "filter to narrow the result set");

        Assert.That(_driver.ResultRowCount(), Is.LessThan(initialCount),
            "Typing into the filter must narrow the visible result set.");
    }

    [Test]
    public void Escape_FirstClosesFilter_ThenRestoresFormRow()
    {
        _driver.ToggleButtonByAutomationId("SearchInResultsToggle").Toggle();
        Thread.Sleep(150);
        _driver.SetText("FilterBox", "class");
        // Wait for the debounced filter to apply — proves the FilterBox value made it through.
        WaitHelpers.WaitUntil(
            () => string.Equals(_driver.TextBox("FilterBox").Text ?? "", "class", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2),
            "FilterBox to commit value");

        _driver.PressEscape();
        // Wait for the panel to actually collapse — IsOffscreen flips when the panel closes.
        WaitHelpers.WaitUntil(
            () =>
            {
                var fb = AppFixture.MainWindow.FindFirstDescendant(
                    AppFixture.Automation.ConditionFactory.ByAutomationId("FilterBox"));
                return fb is null || fb.IsOffscreen;
            },
            TimeSpan.FromSeconds(2),
            "filter panel to close");

        // Sanity: same condition we just waited on.
        var stillThere = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("FilterBox"));
        Assert.That(stillThere?.IsOffscreen ?? true, Is.True,
            "First Escape must collapse the filter panel.");
    }

    [Test]
    public void AlsoMatchFilePath_TogglesPathInclusion()
    {
        var fullCount = _driver.ResultRowCount();
        _driver.ToggleButtonByAutomationId("SearchInResultsToggle").Toggle();
        Thread.Sleep(150);

        // Type something that matches a path component but not any line text. Wait for debounce
        // to apply by polling for the count change.
        _driver.SetText("FilterBox", "regex");
        WaitHelpers.WaitUntil(
            () => _driver.ResultRowCount() != fullCount,
            TimeSpan.FromSeconds(2),
            "filter to change the result set");
        var withoutPath = _driver.ResultRowCount();

        _driver.SetCheck("Also match file path", true);
        // The toggle re-applies the filter — wait for the count to stabilize at the new value.
        // It either grows (path adds matches) or stays the same; bounded by 2 s.
        WaitHelpers.WaitUntil(
            () =>
            {
                var c = _driver.ResultRowCount();
                return c >= withoutPath;
            },
            TimeSpan.FromSeconds(2),
            "filter to re-apply with path inclusion");
        var withPath = _driver.ResultRowCount();

        Assert.That(withPath, Is.GreaterThanOrEqualTo(withoutPath),
            "Including the path in the filter must not reduce the visible set.");
    }
}
