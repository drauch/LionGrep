using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace LionGrep.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class ReplaceTests
{
    private AppDriver _driver = null!;
    private string _isolatedDir = string.Empty;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
        _driver.ResetForm();

        // Each replace test gets its own writable corpus — we're going to mutate files on disk.
        _isolatedDir = CorpusBuilder.CreateIsolated("replace");
        File.WriteAllText(Path.Combine(_isolatedDir, "a.cs"), "alpha bravo alpha\n");
        File.WriteAllText(Path.Combine(_isolatedDir, "b.cs"), "alpha charlie\n");
        File.WriteAllText(Path.Combine(_isolatedDir, "untouched.cs"), "no token here\n");

        _driver.SetText("SearchInBox", _isolatedDir);
        _driver.SetText("SearchPatternBox", "alpha");
        _driver.SetCheck("Case sensitive", true);
        // PreserveCase defaults to true in the ViewModel; without disabling it, replacing
        // "alpha" with "OMEGA" yields "omega" (case copied from match) and the test fails.
        _driver.SetCheck("Preserve case", false);
        _driver.SetText("ReplacePatternBox", "OMEGA");
        _driver.TriggerSearch();
        _driver.WaitForSearchToFinish();
    }

    [TearDown]
    public void TearDown()
    {
        CorpusBuilder.DeleteIfExists(_isolatedDir);
    }

    [Test]
    public void CtrlAltEnter_OpensDialog_AndReplaceWithoutBackupsWorks()
    {
        // GitHub-hosted runners don't reliably route raw keyboard input (Ctrl+Alt+Enter in
        // particular) to the LionGrep window — neither SetForegroundWindow nor AttachThreadInput
        // helps. Skip on CI; the test still has value locally where the accelerator path works.
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("Keyboard accelerator tests require an interactive desktop; skipped on CI.");

        // Ctrl+Alt+Enter now opens the same 3-way confirmation dialog as the main Replace button
        // (per UX request). The "no backups" path is reachable via the dialog's secondary button.
        _driver.TriggerReplaceImmediate();

        var noBak = WaitHelpers.WaitFor(
            () => AppFixture.Automation.GetDesktop().FindFirstDescendant(
                AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                    .And(AppFixture.Automation.ConditionFactory.ByName("Replace w/o backups"))),
            description: "'Replace w/o backups' button on 3-way dialog");
        noBak.AsButton().Invoke();
        Thread.Sleep(800);

        Assert.That(File.ReadAllText(Path.Combine(_isolatedDir, "a.cs")), Is.EqualTo("OMEGA bravo OMEGA\n"));
        Assert.That(File.ReadAllText(Path.Combine(_isolatedDir, "b.cs")), Is.EqualTo("OMEGA charlie\n"));
        Assert.That(File.Exists(Path.Combine(_isolatedDir, "a.cs.lgbak")), Is.False, "No-backup path must not write backup files.");
    }

    [Test]
    public void ReplaceButton_OpensThreeWayDialog_CancelLeavesFilesUntouched()
    {
        var originalA = File.ReadAllText(Path.Combine(_isolatedDir, "a.cs"));
        var replaceBtn = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("ReplaceSplit"));
        Assert.That(replaceBtn, Is.Not.Null, "ReplaceSplit not found.");
        replaceBtn!.AsButton().Invoke();

        var cancel = WaitHelpers.WaitFor(
            () => AppFixture.Automation.GetDesktop().FindFirstDescendant(
                AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                    .And(AppFixture.Automation.ConditionFactory.ByName("Cancel"))),
            description: "Cancel button on 3-way dialog");
        cancel.AsButton().Invoke();
        Thread.Sleep(300);

        Assert.That(File.ReadAllText(Path.Combine(_isolatedDir, "a.cs")), Is.EqualTo(originalA));
    }

    [Test]
    public void Undo_WhenBackupIsMissing_ReportsFailedCount()
    {
        // R14 — Undo's "failed++" path is exercised when the user manually deletes a backup file
        // between the replace and the undo. Verify the summary acknowledges the failure rather
        // than silently restoring zero files.
        var replaceBtn = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("ReplaceSplit"));
        Assert.That(replaceBtn, Is.Not.Null, "ReplaceSplit not found.");
        replaceBtn!.AsButton().Invoke();
        var primary = WaitHelpers.WaitFor(
            () => AppFixture.Automation.GetDesktop().FindFirstDescendant(
                AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                    .And(AppFixture.Automation.ConditionFactory.ByName("Replace with backups"))),
            description: "'Replace with backups' button on 3-way dialog");
        primary.AsButton().Invoke();
        Thread.Sleep(800);

        // Sabotage: delete the backup files we just wrote.
        foreach (var bak in Directory.GetFiles(_isolatedDir, "*.lgbak"))
            File.Delete(bak);

        _driver.ButtonByContent("Undo").Invoke();
        Thread.Sleep(800);

        var summary = _driver.ReadResultsSummary();
        Assert.That(summary, Does.Contain("failed").IgnoreCase,
            "Undo summary should mention the missing-backup failures, not silently report 0 restored.");
    }

    [Test]
    public void ReplaceWithBackups_WritesBackupFiles_AndUndoRestoresThem()
    {
        var originalA = File.ReadAllText(Path.Combine(_isolatedDir, "a.cs"));

        var replaceBtn = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("ReplaceSplit"));
        Assert.That(replaceBtn, Is.Not.Null, "ReplaceSplit not found.");
        replaceBtn!.AsButton().Invoke();

        // Click "Replace with backups" (the Primary button on the dialog).
        var primary = WaitHelpers.WaitFor(
            () => AppFixture.Automation.GetDesktop().FindFirstDescendant(
                AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                    .And(AppFixture.Automation.ConditionFactory.ByName("Replace with backups"))),
            description: "'Replace with backups' button on 3-way dialog");
        primary.AsButton().Invoke();
        Thread.Sleep(800);

        // After replace: contents changed AND backup files exist.
        Assert.That(File.ReadAllText(Path.Combine(_isolatedDir, "a.cs")), Is.Not.EqualTo(originalA));
        Assert.That(File.Exists(Path.Combine(_isolatedDir, "a.cs.lgbak")), Is.True);

        // Undo restores from the backup file.
        _driver.ButtonByContent("Undo").Invoke();
        Thread.Sleep(800);

        Assert.That(File.ReadAllText(Path.Combine(_isolatedDir, "a.cs")), Is.EqualTo(originalA));
        Assert.That(File.Exists(Path.Combine(_isolatedDir, "a.cs.lgbak")), Is.False, "Undo deletes the backup file after restoring.");
    }
}
