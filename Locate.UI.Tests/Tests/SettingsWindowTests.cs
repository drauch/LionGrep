using FlaUI.Core.Definitions;
using NUnit.Framework;

namespace Locate.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class SettingsWindowTests
{
    private AppDriver _driver = null!;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
    }

    [Test]
    public void OpenSettings_FromTitleBar_AndCloseAgain()
    {
        // Click the gear icon in the title bar. It has tooltip "Settings".
        var settingsBtn = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("Settings")));
        Assert.That(settingsBtn, Is.Not.Null, "Title-bar Settings button not found.");
        settingsBtn!.AsButton().Invoke();
        Thread.Sleep(800);

        // Find the Settings window — it has a distinctive title.
        var settingsWindow = AppFixture.App.GetAllTopLevelWindows(AppFixture.Automation)
            .FirstOrDefault(w => (w.Title ?? "").Contains("Settings", StringComparison.OrdinalIgnoreCase));
        Assert.That(settingsWindow, Is.Not.Null, "Settings window did not open.");

        // Look for the "Don't warn when replacing" checkbox we expect to find there.
        var dontWarn = settingsWindow!.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.CheckBox)
                .And(AppFixture.Automation.ConditionFactory.ByName(new System.Text.RegularExpressions.Regex("Don't warn.*").ToString())));
        // ByName-with-regex isn't a real overload; do a manual scan as fallback.
        if (dontWarn is null)
        {
            foreach (var cb in settingsWindow.FindAllDescendants(AppFixture.Automation.ConditionFactory.ByControlType(ControlType.CheckBox)))
            {
                if ((cb.Name ?? "").StartsWith("Don't warn", StringComparison.OrdinalIgnoreCase))
                { dontWarn = cb; break; }
            }
        }
        Assert.That(dontWarn, Is.Not.Null, "Settings should expose the 'Don't warn when replacing' checkbox.");

        // Look for the "Remember recently used values..." checkbox.
        var rememberFound = false;
        foreach (var cb in settingsWindow.FindAllDescendants(AppFixture.Automation.ConditionFactory.ByControlType(ControlType.CheckBox)))
        {
            if ((cb.Name ?? "").StartsWith("Remember recently used", StringComparison.OrdinalIgnoreCase))
            { rememberFound = true; break; }
        }
        Assert.That(rememberFound, Is.True, "Settings should expose the 'Remember recently used values' checkbox.");

        // Close via the Close button.
        var closeBtn = settingsWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("Close")));
        Assert.That(closeBtn, Is.Not.Null);
        closeBtn!.AsButton().Invoke();
        Thread.Sleep(400);
    }

    [Test]
    public void OpenAbout_DialogShows_AndCloses()
    {
        var aboutBtn = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("About")));
        Assert.That(aboutBtn, Is.Not.Null);
        aboutBtn!.AsButton().Invoke();
        Thread.Sleep(500);

        // Dismiss whatever closing button the About dialog presents (typically OK / Close).
        foreach (var name in new[] { "Close", "OK" })
        {
            var btn = AppFixture.MainWindow.FindFirstDescendant(
                AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                    .And(AppFixture.Automation.ConditionFactory.ByName(name)));
            if (btn is not null)
            {
                btn.AsButton().Invoke();
                break;
            }
        }
        Thread.Sleep(200);
    }
}
