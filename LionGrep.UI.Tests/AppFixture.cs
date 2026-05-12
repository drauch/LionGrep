using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Win32;
using NUnit.Framework;
using System.Diagnostics;

namespace LionGrep.UI.Tests;

/// <summary>
/// Assembly-level setup. Launches LionGrep.exe once for the entire run, then closes it at the end.
/// Tests share the running process; each test must reset the form state itself before running.
///
/// Why no FlaUI Application object: WinAppSDK 2.0 self-contained startup re-spawns the app under
/// a new PID one or more times before settling, so tracking the PID Process.Start returns is futile.
/// Instead, we poll the process list for a LionGrep instance with a stable MainWindowHandle and
/// wrap that HWND with UIA. From there, everything is HWND-rooted and process-lifecycle-agnostic.
///
/// State sandboxing: each run picks a fresh subkey under HKCU\Software\LionGrepUITests\&lt;guid&gt;
/// and passes it to the app via --alternate-registry-key. The developer's real
/// HKCU\Software\LionGrep is therefore never read or written during the run. The sandbox subkey
/// is wiped in the OneTimeTearDown so nothing accumulates from previous runs.
/// </summary>
[SetUpFixture]
public sealed class AppFixture
{
    public static UIA3Automation Automation { get; private set; } = null!;
    public static Window MainWindow { get; private set; } = null!;
    public static string ReadOnlyCorpus { get; private set; } = string.Empty;
    public static string SandboxRegistryPath { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public void LaunchApp()
    {
        ReadOnlyCorpus = CorpusBuilder.BuildReadOnlyCorpus();
        SandboxRegistryPath = $@"Software\LionGrepUITests\{Guid.NewGuid():N}";

        var exe = LionGrepBuiltExe();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--alternate-registry-key");
        psi.ArgumentList.Add(SandboxRegistryPath);

        // Fire-and-forget. WinAppSDK 2.0 self-contained startup forks the process at least once;
        // tracking the launcher PID is useless. The launcher exits within ~1s of spawn.
        using (var launcher = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start LionGrep.exe."))
        {
            launcher.Dispose();
        }

        var exeName = Path.GetFileNameWithoutExtension(exe);
        var hwnd = WaitForStableWindow(exeName, TimeSpan.FromSeconds(30))
            ?? throw new InvalidOperationException(
                $"No stable {exeName} process with a main window appeared within 30s.");

        Automation = new UIA3Automation();
        MainWindow = Automation.FromHandle(hwnd).AsWindow();

        // Give the WinUI dispatcher a beat to finish first-render layout (form-fit deferred dispatch).
        Thread.Sleep(2_000);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        // Catching System.Exception is intentional: teardown must succeed even if FlaUI throws
        // anything from a half-collapsed UIA tree or an already-dead test process.
#pragma warning disable RCS1075, IDISP007
        try { MainWindow?.Close(); } catch (Exception) { /* ignore */ }
        try { Automation?.Dispose(); } catch (Exception) { /* ignore */ }

        // Belt-and-braces: if Close didn't terminate the app (e.g. a modal blocked it), nuke any
        // surviving LionGrep processes so the next run starts clean.
        foreach (var p in Process.GetProcessesByName("LionGrep"))
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception) { /* best effort */ }
            try { p.Dispose(); } catch (Exception) { /* best effort */ }
        }
#pragma warning restore RCS1075, IDISP007

        // Wipe the sandbox subkey so artefacts of this run don't accumulate. Best-effort —
        // if the dev wants to inspect it post-mortem, they can re-run with a debugger and
        // skip teardown.
        TryDeleteSandbox();
    }

    private static void TryDeleteSandbox()
    {
        if (string.IsNullOrEmpty(SandboxRegistryPath)) return;
        try { Registry.CurrentUser.DeleteSubKeyTree(SandboxRegistryPath, throwOnMissingSubKey: false); }
        catch { /* best-effort cleanup */ }
        // Also try to remove the parent "LionGrepUITests" container if it's now empty,
        // so it doesn't linger between runs.
        try
        {
            using var parent = Registry.CurrentUser.OpenSubKey(@"Software\LionGrepUITests", writable: true);
            if (parent is not null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
                Registry.CurrentUser.DeleteSubKey(@"Software\LionGrepUITests", throwOnMissingSubKey: false);
        }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Locates the most recently built LionGrep.exe by walking up from the test bin folder. Tries
    /// Debug then Release configurations and the two possible layouts emitted by `dotnet build`:
    /// with a `win-x64\` RID subfolder (when the local publish profile auto-applies) and without
    /// (the bare TFM output dir on a fresh runner). Throws a clear error if nothing's been built
    /// yet — the suite isn't going to magically build the WinUI app.
    /// </summary>
    private static string LionGrepBuiltExe()
    {
        var binBaseSegments = new[] { "LionGrep", "bin", "x64" };
        var tfm = "net10.0-windows10.0.19041.0";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var tfmDir = Path.Combine(new[] { dir.FullName }.Concat(binBaseSegments).Append(config).Append(tfm).ToArray());

                // 1) Bare TFM output (framework-dependent build on a fresh runner / CI default).
                var bare = Path.Combine(tfmDir, "LionGrep.exe");
                if (File.Exists(bare)) return bare;

                // 2) RID-suffixed output (local builds when win-x64.pubxml gets auto-applied,
                //    or any self-contained build).
                var rid = Path.Combine(tfmDir, "win-x64", "LionGrep.exe");
                if (File.Exists(rid)) return rid;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not find LionGrep.exe in any sibling LionGrep/bin/x64/{Debug,Release}/" +
            "net10.0-windows10.0.19041.0/(win-x64/)?. " +
            "Build it first: `dotnet build LionGrep/LionGrep.csproj -c Debug -p:Platform=x64`.");
    }

    /// <summary>Polls for a process named <paramref name="exeName"/> with a non-zero main window
    /// handle, then waits a settle interval and re-checks the SAME pid/hwnd still exists. If the
    /// process has been replaced (WinAppSDK re-spawn — the launcher forks into 2+ generations
    /// before settling), we loop. Returns the stable HWND or null on timeout.</summary>
    private static IntPtr? WaitForStableWindow(string exeName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            (int pid, IntPtr hwnd)? snapshot = null;
            foreach (var p in Process.GetProcessesByName(exeName))
            {
                using (p)
                {
                    if (snapshot is not null) continue;
                    try
                    {
                        p.Refresh();
                        if (p.MainWindowHandle != IntPtr.Zero) snapshot = (p.Id, p.MainWindowHandle);
                    }
                    catch (InvalidOperationException) { /* exited mid-enumeration */ }
                }
            }

            if (snapshot is null) { Thread.Sleep(200); continue; }

            // Settle: if the same pid/hwnd still resolves after a delay, we trust it.
            Thread.Sleep(1500);
            try
            {
                using var p = Process.GetProcessById(snapshot.Value.pid);
                p.Refresh();
                if (p.MainWindowHandle == snapshot.Value.hwnd) return snapshot.Value.hwnd;
            }
            catch (ArgumentException) { /* re-spawn happened during the settle — loop */ }
        }
        return null;
    }
}
