using FlaUI.Core.Definitions;
using NUnit.Framework;

namespace Locate.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class SortTests
{
    private AppDriver _driver = null!;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
        _driver.ResetForm();
        _driver.SetText("SearchPatternBox", "class");
        _driver.SetCheck("Case sensitive", true);
        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();
    }

    [Test]
    public void Header_Click_Cycles_Asc_Desc_None()
    {
        var nameHeader = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("NameHeader"));
        Assert.That(nameHeader, Is.Not.Null);

        // Initial: not sorted by name. Click → Ascending → arrow ▲.
        nameHeader!.Click();
        Thread.Sleep(150);
        Assert.That(nameHeader.Name ?? "", Does.Contain("▲"));

        nameHeader.Click();
        Thread.Sleep(150);
        Assert.That(nameHeader.Name ?? "", Does.Contain("▼"));

        nameHeader.Click();
        Thread.Sleep(150);
        Assert.That(nameHeader.Name ?? "", Does.Not.Contain("▲").And.Not.Contains("▼"));
    }
}
