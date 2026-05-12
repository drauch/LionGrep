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
        => WaitHelpers.WaitFor(
            () => _window.FindFirstDescendant(_cf.ByAutomationId("ResultsList")),
            description: "ResultsList").AsListBox();

    private static AutomationElement Required(AutomationElement? element, string description)
        => element ?? throw new InvalidOperationException(
            $"UIA element not found: {description}. The window may not be ready, or the AutomationId/Name "
            + "may have changed in XAML. Inspect with FlaUInspect / accessibility tree to confirm.");

    // ---- Form helpers ------------------------------------------------------

    public void SetText(string automationId, string text)
    {
        // ValuePattern.SetValue is atomic and doesn't depend on the LionGrep window having OS
        // focus. The previous focus+Text=""+Enter() pattern was reliable locally but fragile on
        // CI runners where the desktop session isn't foregrounded — Enter() emits keystrokes,
        // and if focus is elsewhere they land in the wrong window, leaving the textbox with its
        // previous content. SetValue goes through UIA directly.
        var tb = TextBox(automationId);
        tb.Focus();                                          // best-effort
        tb.Patterns.Value.Pattern.SetValue(text);
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

    /// <summary>Triggers a search using whichever path lands first: UIA Invoke on the SearchSplit's
    /// primary button (works without OS focus, our usual win on CI), with a Ctrl+Enter keyboard
    /// fallback (good locally where keyboard input reaches the foregrounded window). One of the
    /// two fires on any host. The 800 ms gate skips the keyboard path when UIA already moved the
    /// summary, avoiding double-trigger and "second search overwrites first" races.</summary>
    public void TriggerSearch()
    {
        var startSummary = ReadResultsSummary();
        var split = _window.FindFirstDescendant(_cf.ByAutomationId("SearchSplit"));
        if (split is not null)
        {
            // In WinUI 3, the SplitButton's primary half is the first inner Button child — but
            // invoking the wrapper works fine in this build, so we keep it simple.
            var target = split.FindFirstChild(_cf.ByControlType(ControlType.Button)) ?? split;
#pragma warning disable RCS1075
            try { target.AsButton().Invoke(); }
            catch (Exception) { /* fall through to keyboard fallback */ }
#pragma warning restore RCS1075
        }

        // If UIA worked, skip the keyboard. We can't always *prove* it worked (identical-result
        // re-searches don't change the summary), so use a short observation window and only fall
        // back if there's no observable change and no "(running)".
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(800);
        while (DateTime.UtcNow < deadline)
        {
            var s = ReadResultsSummary();
            if (s.Contains("(running)") || !string.Equals(s, startSummary, StringComparison.Ordinal))
                return;
            Thread.Sleep(80);
        }

#pragma warning disable S2325
        BringToForeground();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.RETURN);
#pragma warning restore S2325
    }

#pragma warning disable S2325

    /// <summary>Ctrl+Alt+Enter.</summary>
    public void TriggerReplaceImmediate()
    {
        BringToForeground();
        // VirtualKeyShort uses LMENU / RMENU for the Alt keys (mirroring Win32 VK_LMENU / VK_RMENU);
        // there's no plain MENU constant, so we send Left-Alt explicitly.
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.LMENU, VirtualKeyShort.RETURN);
    }

    public void PressEscape()
    {
        BringToForeground();
        Keyboard.Press(VirtualKeyShort.ESCAPE);
    }
#pragma warning restore S2325

    /// <summary>Brings the LionGrep window to the OS foreground best-effort, so keyboard input
    /// reaches the accelerator scope. Plain <see cref="NativeMethods.SetForegroundWindow"/>
    /// (subject to Windows' "can't steal focus" rules) is good enough for most cases; the
    /// AttachThreadInput escalation we tried was destabilising the test process. Exposed
    /// publicly for accelerator-specific tests.</summary>
    public void BringToForeground()
    {
        var hwnd = _window.Properties.NativeWindowHandle.ValueOrDefault;
        if (hwnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(hwnd);
        Thread.Sleep(40);
    }

    /// <summary>Waits until the results summary no longer shows "(running)".
    /// <para>Tries to detect the new search starting (summary changed, or "(running)" present)
    /// within 3 s as a best-effort signal — but doesn't throw if neither happens. Reason: on
    /// fast hosts the search can complete with output identical to the previous search before we
    /// catch the "(running)" intermediate state, leaving us unable to tell a fast same-result
    /// search from a no-op. Throwing would produce a misleading failure. If no search actually
    /// ran, the test's own assertion will catch it with a clearer error.</para></summary>
    public void WaitForSearchToFinish(TimeSpan? timeout = null)
    {
        var startSummary = ReadResultsSummary();
        // Phase 1 (best-effort, no throw): observe the new search starting.
        var phase1Deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < phase1Deadline)
        {
            var s = ReadResultsSummary();
            if (s.Contains("(running)") || !string.Equals(s, startSummary, StringComparison.Ordinal))
                break;
            Thread.Sleep(80);
        }
        // Phase 2: any "(running)" state clears.
        WaitHelpers.WaitUntil(
            () => { var s = ReadResultsSummary(); return !string.IsNullOrEmpty(s) && !s.Contains("(running)"); },
            timeout ?? TimeSpan.FromSeconds(30),
            "search to finish");
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
