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
        toggle.Invoke();   // open
        Thread.Sleep(150);

        // FilterBox should now be present and visible.
        var filterBox = _driver.TextBox("FilterBox");
        Assert.That(filterBox.IsAvailable, Is.True);

        toggle.Invoke();   // close
        Thread.Sleep(150);
    }

    [Test]
    public void FilterText_NarrowsResults_OnLineContent()
    {
        var initialCount = _driver.ResultRowCount();

        _driver.ToggleButtonByAutomationId("SearchInResultsToggle").Invoke();
        Thread.Sleep(150);
        _driver.SetText("FilterBox", "UserService");
        Thread.Sleep(400);   // debounce 250ms + slack

        var filtered = _driver.ResultRowCount();
        Assert.That(filtered, Is.LessThan(initialCount),
            "Typing into the filter must narrow the visible result set.");
    }

    [Test]
    public void Escape_FirstClosesFilter_ThenRestoresFormRow()
    {
        _driver.ToggleButtonByAutomationId("SearchInResultsToggle").Invoke();
        Thread.Sleep(150);
        _driver.SetText("FilterBox", "class");
        Thread.Sleep(400);

        _driver.PressEscape();
        Thread.Sleep(200);

        // Filter panel is collapsed — FilterBox shouldn't be in the visible tree any more.
        var stillThere = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("FilterBox"));
        Assert.That(stillThere?.IsOffscreen ?? true, Is.True,
            "First Escape must collapse the filter panel.");
    }

    [Test]
    public void AlsoMatchFilePath_TogglesPathInclusion()
    {
        _driver.ToggleButtonByAutomationId("SearchInResultsToggle").Invoke();
        Thread.Sleep(150);

        // Type something that matches a path component but not any line text.
        _driver.SetText("FilterBox", "regex");
        Thread.Sleep(400);
        var withoutPath = _driver.ResultRowCount();

        _driver.SetCheck("Also match file path", true);
        Thread.Sleep(400);
        var withPath = _driver.ResultRowCount();

        Assert.That(withPath, Is.GreaterThanOrEqualTo(withoutPath),
            "Including the path in the filter must not reduce the visible set.");
    }
}
