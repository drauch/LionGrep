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

    /// <summary>Best-effort window focus that swallows the UIA "subscribers couldn't invoke"
    /// exception that occasionally fires on Focus() late in long test runs (HWND may be in a
    /// transient state). Returning without a focused window is fine — the subsequent keyboard
    /// input lands on whatever has focus, which is usually still the LionGrep window.</summary>
    private void TryFocusWindow()
    {
        try { _window.Focus(); Thread.Sleep(50); }
        catch (System.Runtime.InteropServices.COMException) { /* HWND in flux, skip */ }
    }

    /// <summary>Restores the form row (collapsed after any search/replace). Tries every recovery
    /// path in turn so that whichever one works lands the form expanded. Cheap and idempotent.</summary>
    public void EnsureFormVisible()
    {
        // 1) Click the in-app "Show query" button when present — direct, no keyboard focus dance.
        var showQuery = _window.FindFirstDescendant(_cf.ByAutomationId("ShowQueryButton"));
        if (showQuery is not null && !showQuery.IsOffscreen)
        {
#pragma warning disable RCS1075
            try { showQuery.AsButton().Invoke(); }
            catch (Exception) { /* try other paths */ }
#pragma warning restore RCS1075
        }

        // 2) Belt-and-braces keyboard Escape × 2 — first closes filter panel if open, second
        // triggers RestoreFormRow via the app's accelerator. No-ops if form is already shown.
        TryFocusWindow();
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
        Thread.Sleep(120);
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
        Thread.Sleep(400);   // give the WinUI dispatcher time to relayout + refresh the UIA tree
    }

    public Button ButtonByContent(string content)
    {
        // Some buttons have stable AutomationIds wired in XAML — prefer those because UIA's Name
        // for buttons whose content is a StackPanel(FontIcon + TextBlock) can be empty or weirdly
        // composed depending on the WinUI build.
        var byId = TryButtonByAutomationIdForContent(content);
        if (byId is not null) return byId;

        var found = FindButtonByContent(content);
        if (found is not null) return found;
        EnsureFormVisible();
        // Re-check the AutomationId map first after the form is restored.
        byId = TryButtonByAutomationIdForContent(content);
        if (byId is not null) return byId;
        found = FindButtonByContent(content);
        if (found is not null) return found;
        throw new InvalidOperationException($"No Button found with content matching '{content}'.");
    }

    /// <summary>Maps common content strings to the AutomationIds set in MainWindow.xaml. Returns
    /// the resolved Button or null if there's no mapping (or the element isn't in the tree yet).</summary>
    private Button? TryButtonByAutomationIdForContent(string content) => content switch
    {
        "Undo" => FindButtonByAutomationId("UndoButton"),
        "Expand all" => FindButtonByAutomationId("ExpandAllButton"),
        "Collapse all" => FindButtonByAutomationId("CollapseAllButton"),
        _ => null,
    };

    private Button? FindButtonByAutomationId(string automationId)
    {
        var e = _window.FindFirstDescendant(_cf.ByAutomationId(automationId));
        return e?.AsButton();
    }

    private Button? FindButtonByContent(string content)
    {
        // Buttons in our UI typically host a StackPanel(FontIcon + TextBlock) for the content; the
        // exposed UIA Name is usually the inner TextBlock text. Try ByName first; fall back to scanning.
        var byName = _window.FindFirstDescendant(_cf.ByControlType(ControlType.Button).And(_cf.ByName(content)));
        if (byName is not null) return byName.AsButton();

        foreach (var b in _window.FindAllDescendants(_cf.ByControlType(ControlType.Button)))
        {
            if ((b.TryGetName() ?? "").Contains(content, StringComparison.OrdinalIgnoreCase))
                return b.AsButton();
        }
        return null;
    }

    public ToggleButton ToggleButtonByAutomationId(string automationId)
        => Required(_window.FindFirstDescendant(_cf.ByAutomationId(automationId)),
            $"ToggleButton with AutomationId='{automationId}'").AsToggleButton();

    public ListBox ResultsList()
    {
        // Short retry: occasionally the UIA peer for the ListView isn't immediately present right
        // after a search completes (the FilteredResults binding triggers a tree update on the next
        // dispatch). One ~150ms re-poll is enough to ride that out.
        var element = _window.FindFirstDescendant(_cf.ByAutomationId("ResultsList"));
        if (element is null)
        {
            Thread.Sleep(150);
            element = _window.FindFirstDescendant(_cf.ByAutomationId("ResultsList"));
        }
        return Required(element, "ResultsList").AsListBox();
    }

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
        // Use the Toggle pattern instead of Click() — Click needs a clickable point, which fails
        // for off-screen or not-yet-laid-out controls (common when the WHAT panel hasn't been
        // scrolled into view). Toggle goes through UIA directly with no mouse coords.
        var cb = CheckBoxByContent(content);
        if (cb.IsChecked != isChecked) cb.Toggle();
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
        // Prefer the dedicated TextBlock by AutomationId — added in XAML so we don't have to
        // disambiguate from button labels like "Undo".
        var direct = _window.FindFirstDescendant(_cf.ByAutomationId("ResultsSummary"));
        if (direct is not null)
        {
            var name = direct.TryGetName();
            if (!string.IsNullOrEmpty(name)) return name!;
        }

        // Fallback: scan TextBlocks for known summary patterns. Be specific so the "Undo" button
        // label (a bare "Undo") doesn't match the "Undo: restored …" status format.
        foreach (var tb in _window.FindAllDescendants(_cf.ByControlType(ControlType.Text)))
        {
            var t = tb.TryGetName() ?? string.Empty;
            if (t.Contains("matches in", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("Replaced ", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("Cancelled", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("Undo:", StringComparison.OrdinalIgnoreCase)
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
        // If a previous test triggered a search/replace, the form row is collapsed and
        // SearchInBox is hidden from the visible UIA tree. EnsureFormVisible prefers clicking
        // the in-app "Show query" button when present (more reliable than keyboard accelerators).
        EnsureFormVisible();

        // Click the WHAT/FILTER Reset buttons. They may or may not fire — UIA Invoke on Reset
        // buttons inside a collapsed form has been observed to silently no-op. The explicit
        // filter-widget reset below is the belt-and-braces guarantee.
        var resetCondition = _cf.ByControlType(ControlType.Button).And(_cf.ByName("Reset"));
        foreach (var btn in _window.FindAllDescendants(resetCondition))
        {
            btn.AsButton().Invoke();
        }

        // Belt-and-braces: explicitly normalize filter widgets so leaked state from one test
        // (e.g. BetweenSizeFilter setting SizeMode=Between/200KB) doesn't break the next.
        ResetFilterWidgetsExplicitly();

        SetText("SearchInBox", AppFixture.ReadOnlyCorpus);
    }

    /// <summary>Forces every filter widget back to a permissive default by manipulating them
    /// directly through UIA, bypassing the Reset-button-bound commands. Idempotent.</summary>
    private void ResetFilterWidgetsExplicitly()
    {
        // Size and Date ComboBoxes: index 0 = "All sizes" / "All dates", which disables those
        // filters in BuildSearchOptions (returns null).
        var combos = _window.FindAllDescendants(_cf.ByControlType(ControlType.ComboBox));
        // Order in the form: Size is the first ComboBox, Date is the second. (See MainWindow.xaml.)
        if (combos.Length >= 1)
        {
            try { combos[0].AsComboBox().Select(0); } catch { /* control may be off-tree if form is collapsed */ }
        }
        if (combos.Length >= 2)
        {
            try { combos[1].AsComboBox().Select(0); } catch { /* same */ }
        }

        // FileNames / ExcludePaths text fields: blank to disable filtering.
        foreach (var id in new[] { "FileNamesBox", "ExcludePathsBox" })
        {
            var tb = _window.FindFirstDescendant(_cf.ByAutomationId(id));
            if (tb is null) continue;
            try
            {
                var box = tb.AsTextBox();
                box.Focus();
                box.Text = "";
            }
            catch { /* element may not be available; ignore */ }
        }
    }
}
