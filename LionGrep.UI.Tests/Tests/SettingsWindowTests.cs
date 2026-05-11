using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using NUnit.Framework;

namespace LionGrep.UI.Tests.Tests;

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

    [TearDown]
    public void TearDown()
    {
        // Defensive: make sure no Settings window leaks into the next test. Test bodies do their
        // own close attempts, but assertion failures can short-circuit them.
        ForceCloseSettingsWindow();
    }

    [Test]
    public void OpenSettings_FromTitleBar_AndCloseAgain()
    {
        var settingsBtn = FindSettingsButton();
        Assert.That(settingsBtn, Is.Not.Null, "Title-bar Settings button not found.");
        settingsBtn!.AsButton().Invoke();
        Thread.Sleep(800);

        var settingsWindow = FindSettingsWindow();
        Assert.That(settingsWindow, Is.Not.Null, "Settings window did not open.");

        // "Don't warn when replacing"
        AutomationElement? dontWarn = null;
        foreach (var cb in settingsWindow!.FindAllDescendants(AppFixture.Automation.ConditionFactory.ByControlType(ControlType.CheckBox)))
        {
            if ((cb.TryGetName() ?? "").StartsWith("Don't warn", StringComparison.OrdinalIgnoreCase))
            { dontWarn = cb; break; }
        }
        Assert.That(dontWarn, Is.Not.Null, "Settings should expose the 'Don't warn when replacing' checkbox.");

        // "Remember recently used values..."
        var rememberFound = false;
        foreach (var cb in settingsWindow.FindAllDescendants(AppFixture.Automation.ConditionFactory.ByControlType(ControlType.CheckBox)))
        {
            if ((cb.TryGetName() ?? "").StartsWith("Remember recently used", StringComparison.OrdinalIgnoreCase))
            { rememberFound = true; break; }
        }
        Assert.That(rememberFound, Is.True, "Settings should expose the 'Remember recently used values' checkbox.");

        ForceCloseSettingsWindow();
    }

    [Test]
    public void ResetEverything_ClearsRegistrySandbox()
    {
        _driver.ResetForm();
        _driver.SetText("SearchPatternBox", CorpusBuilder.OnlyInOne);
        _driver.SetCheck("Case sensitive", true);
        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();

        using (var sandbox = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppFixture.SandboxRegistryPath))
        {
            Assert.That(sandbox, Is.Not.Null, "Sandbox key should now exist (search just wrote LastForm + recents).");
            Assert.That(sandbox!.SubKeyCount + sandbox.ValueCount, Is.GreaterThan(0));
        }

        var settingsBtn = FindSettingsButton();
        Assert.That(settingsBtn, Is.Not.Null, "Title-bar Settings button not found.");
        settingsBtn!.AsButton().Invoke();
        Thread.Sleep(800);

        var settingsWindow = FindSettingsWindow();
        Assert.That(settingsWindow, Is.Not.Null, "Settings window did not open.");

        var resetBtn = settingsWindow!.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("Reset everything…")));
        Assert.That(resetBtn, Is.Not.Null, "Reset everything… button should be present in Settings.");
        resetBtn!.AsButton().Invoke();
        Thread.Sleep(500);

        // ContentDialog confirmation lives in a popup tree — search the desktop.
        var confirm = AppFixture.Automation.GetDesktop().FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("Reset everything")));
        Assert.That(confirm, Is.Not.Null, "Reset confirmation dialog should expose a 'Reset everything' button.");
        confirm!.AsButton().Invoke();
        Thread.Sleep(800);

        using var afterReset = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AppFixture.SandboxRegistryPath);
        Assert.That(afterReset, Is.Null, "Reset everything must delete the entire app subtree.");

        ForceCloseSettingsWindow();
    }

    [Test]
    public void OpenAbout_DialogShows_AndCloses()
    {
        var aboutBtn = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("AboutButton"));
        Assert.That(aboutBtn, Is.Not.Null);
        aboutBtn!.AsButton().Invoke();
        Thread.Sleep(500);

        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
        Thread.Sleep(400);
    }

    // ---- Helpers ----

    private static AutomationElement? FindSettingsButton()
    {
        return AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("SettingsButton"))
            ?? AppFixture.MainWindow.FindFirstDescendant(
                AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                    .And(AppFixture.Automation.ConditionFactory.ByName("Settings")));
    }

    /// <summary>Finds the Settings window by enumerating HWNDs in any LionGrep process, then
    /// filtering by window title. Bypasses UIA tree quirks where the WinUI secondary window
    /// might not be a direct child of Desktop. Returns an AutomationElement wrapper or null.</summary>
    private static AutomationElement? FindSettingsWindow()
    {
        var hwnd = FindSettingsHwnd();
        if (hwnd == IntPtr.Zero) return null;
        try { return AppFixture.Automation.FromHandle(hwnd); }
        catch { return null; }
    }

    private static IntPtr FindSettingsHwnd()
    {
        var liongrepPids = Process.GetProcessesByName("LionGrep").Select(p => { var id = p.Id; p.Dispose(); return (uint)id; }).ToHashSet();
        if (liongrepPids.Count == 0) return IntPtr.Zero;

        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            if (!liongrepPids.Contains(pid)) return true;
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString().Contains("Settings", StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;   // stop enumeration
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Closes any leaked Settings window. Tries the polite path first (focus + Escape so
    /// the app's OnEscapePressed handler runs Cancel semantics), then falls back to WM_CLOSE.</summary>
    private static void ForceCloseSettingsWindow()
    {
        var hwnd = FindSettingsHwnd();
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var win = AppFixture.Automation.FromHandle(hwnd);
            win.Focus();
            Thread.Sleep(80);
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESCAPE);
            Thread.Sleep(300);
        }
        catch { /* fall through to brute force */ }

        // Verify it's actually gone — if not, send WM_CLOSE directly.
        if (FindSettingsHwnd() == IntPtr.Zero) return;
        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Thread.Sleep(200);
    }

    // ---- Win32 ----

    private const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
