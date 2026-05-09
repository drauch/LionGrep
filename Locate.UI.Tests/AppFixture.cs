using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Locate.UI.Tests;

/// <summary>
/// Assembly-level setup. Launches Locate.exe once for the entire run, then closes it at the end.
/// Tests share the running process; each test must reset the form state itself before running.
/// </summary>
[SetUpFixture]
public sealed class AppFixture
{
    public static Application App { get; private set; } = null!;
    public static UIA3Automation Automation { get; private set; } = null!;
    public static Window MainWindow { get; private set; } = null!;
    public static string ReadOnlyCorpus { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public void LaunchApp()
    {
        ReadOnlyCorpus = CorpusBuilder.BuildReadOnlyCorpus();

        var exe = LocateBuiltExe();
        App = Application.Launch(exe);
        Automation = new UIA3Automation();
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(20))
            ?? throw new InvalidOperationException("Locate window did not appear within 20s.");

        // Give the WinUI dispatcher a beat to finish first-render layout (form-fit deferred dispatch).
        Thread.Sleep(2_000);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        try { App.Close(); App.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(5)); } catch { /* swallow */ }
        try { App.Dispose(); } catch { }
        try { Automation.Dispose(); } catch { }
    }

    /// <summary>
    /// Locates the most recently built Locate.exe by walking up from the test bin folder. Tries
    /// Debug then Release. Throws a clear error if nothing's been built yet — the suite isn't
    /// going to magically build the WinUI app, the dev runs `dotnet build Locate -c Debug -p:Platform=x64`
    /// before kicking these off.
    /// </summary>
    private static string LocateBuiltExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName, "Locate", "bin", "x64", config,
                    "net10.0-windows10.0.19041.0", "win-x64", "Locate.exe");
                if (File.Exists(candidate)) return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not find Locate.exe in any sibling Locate/bin/x64/{Debug,Release}/.../win-x64/. " +
            "Build it first: `dotnet build Locate/Locate.csproj -c Debug -p:Platform=x64`.");
    }
}
