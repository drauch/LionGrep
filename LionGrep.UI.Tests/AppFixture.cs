using FlaUI.Core;
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
/// State sandboxing: each run picks a fresh subkey under HKCU\Software\LionGrepUITests\&lt;guid&gt;
/// and passes it to the app via --alternate-registry-key. The developer's real
/// HKCU\Software\LionGrep is therefore never read or written during the run. The sandbox subkey
/// is wiped in the OneTimeTearDown so nothing accumulates from previous runs.
/// </summary>
[SetUpFixture]
public sealed class AppFixture
{
    public static Application App { get; private set; } = null!;
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

        App = Application.Launch(psi);
        Automation = new UIA3Automation();
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(20))
            ?? throw new InvalidOperationException("LionGrep window did not appear within 20s.");

        // Give the WinUI dispatcher a beat to finish first-render layout (form-fit deferred dispatch).
        Thread.Sleep(2_000);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        // Catching System.Exception is intentional: teardown must succeed even if FlaUI throws
        // anything from a half-collapsed UIA tree or an already-dead test process.
#pragma warning disable RCS1075, IDISP007
        try { App.Close(); App.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(5)); } catch (Exception) { /* ignore */ }
        try { App.Dispose(); } catch (Exception) { /* ignore */ }
        try { Automation.Dispose(); } catch (Exception) { /* ignore */ }
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
    /// LionGreps the most recently built LionGrep.exe by walking up from the test bin folder. Tries
    /// Debug then Release. Throws a clear error if nothing's been built yet — the suite isn't
    /// going to magically build the WinUI app, the dev runs `dotnet build LionGrep -c Debug -p:Platform=x64`
    /// before kicking these off.
    /// </summary>
    private static string LionGrepBuiltExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName, "LionGrep", "bin", "x64", config,
                    "net10.0-windows10.0.19041.0", "win-x64", "LionGrep.exe");
                if (File.Exists(candidate)) return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not find LionGrep.exe in any sibling LionGrep/bin/x64/{Debug,Release}/.../win-x64/. " +
            "Build it first: `dotnet build LionGrep/LionGrep.csproj -c Debug -p:Platform=x64`.");
    }
}
