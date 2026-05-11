using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace LionGrep.UI.Tests;

/// <summary>
/// High-level helpers wrapped around the running LionGrep window. Each test class instantiates one
/// of these in its [SetUp]; the helper methods are written to be readable assertions about
/// product behavior, not raw FlaUI plumbing.
/// </summary>
internal sealed class AppDriver
{
    private readonly Window _window;
    private readonly ConditionFactory _cf;

    public AppDriver()
    {
        _window = AppFixture.MainWindow;
        _cf = AppFixture.Automation.ConditionFactory;
    }

    // ---- LionGrep elements ---------------------------------------------------

    public TextBox TextBox(string automationId)
        => Required(_window.FindFirstDescendant(_cf.ByAutomationId(automationId)),
            $"TextBox with AutomationId='{automationId}'").AsTextBox();

    public CheckBox CheckBoxByContent(string content)
        => Required(_window.FindFirstDescendant(_cf.ByControlType(ControlType.CheckBox).And(_cf.ByName(content))),
            $"CheckBox with content '{content}'").AsCheckBox();

    public Button ButtonByContent(string content)
    {
        // Buttons in our UI typically host a StackPanel(FontIcon + TextBlock) for the content; the
        // exposed UIA Name is usually the inner TextBlock text. Try ByName first; fall back to scanning.
        var byName = _window.FindFirstDescendant(_cf.ByControlType(ControlType.Button).And(_cf.ByName(content)));
        if (byName is not null) return byName.AsButton();

        foreach (var b in _window.FindAllDescendants(_cf.ByControlType(ControlType.Button)))
        {
            if ((b.Name ?? "").Contains(content, StringComparison.OrdinalIgnoreCase))
                return b.AsButton();
        }
        throw new InvalidOperationException($"No Button found with content matching '{content}'.");
    }

    public Button ToggleButtonByAutomationId(string automationId)
        => Required(_window.FindFirstDescendant(_cf.ByAutomationId(automationId)),
            $"ToggleButton with AutomationId='{automationId}'").AsButton();

    public ListBox ResultsList()
        => Required(_window.FindFirstDescendant(_cf.ByAutomationId("ResultsList")),
            "ResultsList").AsListBox();

    private static AutomationElement Required(AutomationElement? element, string description)
        => element ?? throw new InvalidOperationException(
            $"UIA element not found: {description}. The window may not be ready, or the AutomationId/Name "
            + "may have changed in XAML. Inspect with FlaUInspect / accessibility tree to confirm.");

    // ---- Form helpers ------------------------------------------------------

    public void SetText(string automationId, string text)
    {
        var tb = TextBox(automationId);
        tb.Focus();
        tb.Text = "";                  // clear
        tb.Enter(text);
    }

    public string GetText(string automationId) => TextBox(automationId).Text;

    public void SetCheck(string content, bool isChecked)
    {
        var cb = CheckBoxByContent(content);
        if (cb.IsChecked != isChecked) cb.Click();
    }

    // ---- Search lifecycle --------------------------------------------------

#pragma warning disable S2325 // Instance methods by API design — every test fixture calls these via `_driver.TriggerXxx()`.
    public void TriggerSearch()
    {
        // Ctrl+Enter is the user-facing keyboard shortcut and bypasses focus assumptions.
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.RETURN);
    }

    public void TriggerReplaceImmediate()
    {
        // Ctrl+Alt+Enter — replace bypassing the 3-way dialog.
        // VirtualKeyShort uses LMENU / RMENU for the Alt keys (mirroring Win32 VK_LMENU / VK_RMENU);
        // there's no plain MENU constant, so we send Left-Alt explicitly.
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.LMENU, VirtualKeyShort.RETURN);
    }

    public void PressEscape() => Keyboard.Press(VirtualKeyShort.ESCAPE);
#pragma warning restore S2325

    /// <summary>Polls the status text until the "(running)" suffix disappears or we time out.</summary>
    public void WaitForSearchToFinish(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            var summary = ReadResultsSummary();
            if (!string.IsNullOrEmpty(summary) && !summary.Contains("(running)"))
                return;
            Thread.Sleep(150);
        }
        throw new TimeoutException($"Search did not finish in {timeout?.TotalSeconds ?? 30}s.");
    }

    public string ReadResultsSummary()
    {
        // Status text isn't named; find any TextBlock whose text matches our format.
        foreach (var tb in _window.FindAllDescendants(_cf.ByControlType(ControlType.Text)))
        {
            var t = tb.Name ?? string.Empty;
            if (t.Contains("matches in", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("Replaced ", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("Cancelled", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("Undo", StringComparison.OrdinalIgnoreCase)
                || t.Contains("No search run yet", StringComparison.OrdinalIgnoreCase)
                || t.Contains("Provide ", StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }
        return string.Empty;
    }

    public int ResultRowCount() => ResultsList().Items.Length;

    // ---- High-level scenarios used by multiple tests -----------------------

    /// <summary>Resets WHAT and FILTER groups (via the section Reset buttons) and re-fills SearchIn
    /// to the read-only corpus. Use at the top of every test for predictable starting state.</summary>
    public void ResetForm()
    {
        // Click the two Reset buttons (one in WHAT header, one in FILTER header).
        // Filter by Name at the UIA level: some WinUI buttons don't expose the Name property,
        // and reading .Name on those throws PropertyNotSupportedException. Letting UIA do the
        // exact-match filter sidesteps that — non-Name elements just don't match.
        var resetCondition = _cf.ByControlType(ControlType.Button).And(_cf.ByName("Reset"));
        foreach (var btn in _window.FindAllDescendants(resetCondition))
        {
            btn.AsButton().Invoke();
        }
        SetText("SearchInBox", AppFixture.ReadOnlyCorpus);
    }
}
