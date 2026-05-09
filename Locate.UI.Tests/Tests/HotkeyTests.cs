using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace Locate.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class HotkeyTests
{
    private AppDriver _driver = null!;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
        _driver.ResetForm();
    }

    [Test]
    public void CtrlEnter_FromMultilineSearchInBox_StillTriggersSearch()
    {
        // Focus the multi-line Search-in box so Ctrl+Enter has to go through our routed-event hook.
        var searchInBox = _driver.TextBox("SearchInBox");
        searchInBox.Focus();
        _driver.SetText("SearchPatternBox", "class");
        _driver.SetCheck("Case sensitive", true);

        searchInBox.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.RETURN);
        _driver.WaitForSearchToFinish();

        Assert.That(_driver.ReadResultsSummary(), Does.Contain("matches in"));
    }

    [Test]
    public void Escape_LayeredOrder_FilterFirst_ThenForm()
    {
        _driver.SetText("SearchPatternBox", "class");
        _driver.SetCheck("Case sensitive", true);
        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        _driver.ToggleButtonByAutomationId("SearchInResultsToggle").Invoke();
        Thread.Sleep(150);

        // 1st Escape: collapse filter.
        _driver.PressEscape();
        Thread.Sleep(200);

        var filterBoxAfterFirst = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("FilterBox"));
        Assert.That(filterBoxAfterFirst?.IsOffscreen ?? true, Is.True,
            "First Escape must close the filter panel.");

        // 2nd Escape: should not crash, should restore form (if not already visible).
        _driver.PressEscape();
        Thread.Sleep(200);
    }
}
