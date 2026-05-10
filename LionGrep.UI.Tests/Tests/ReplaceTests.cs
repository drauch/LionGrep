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
    public void CtrlAltEnter_ReplacesImmediately_NoBackup()
    {
        _driver.TriggerReplaceImmediate();
        Thread.Sleep(800);   // give the replace a moment

        Assert.That(File.ReadAllText(Path.Combine(_isolatedDir, "a.cs")), Is.EqualTo("OMEGA bravo OMEGA\n"));
        Assert.That(File.ReadAllText(Path.Combine(_isolatedDir, "b.cs")), Is.EqualTo("OMEGA charlie\n"));
        Assert.That(File.Exists(Path.Combine(_isolatedDir, "a.cs.lgbak")), Is.False, "Bypass route must not write backup files.");
    }

    [Test]
    public void ReplaceButton_OpensThreeWayDialog_CancelLeavesFilesUntouched()
    {
        var originalA = File.ReadAllText(Path.Combine(_isolatedDir, "a.cs"));
        var replaceBtn = _driver.ButtonByContent("Replace…");
        replaceBtn.Invoke();
        Thread.Sleep(400);

        // Click the Cancel button on the ContentDialog.
        var cancel = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("Cancel")));
        Assert.That(cancel, Is.Not.Null, "3-way dialog should expose a Cancel button.");
        cancel!.AsButton().Invoke();
        Thread.Sleep(300);

        Assert.That(File.ReadAllText(Path.Combine(_isolatedDir, "a.cs")), Is.EqualTo(originalA));
    }

    [Test]
    public void Undo_WhenBackupIsMissing_ReportsFailedCount()
    {
        // R14 — Undo's "failed++" path is exercised when the user manually deletes a backup file
        // between the replace and the undo. Verify the summary acknowledges the failure rather
        // than silently restoring zero files.
        var replaceBtn = _driver.ButtonByContent("Replace…");
        replaceBtn.Invoke();
        Thread.Sleep(400);
        var primary = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("Replace with backups")));
        primary!.AsButton().Invoke();
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

        var replaceBtn = _driver.ButtonByContent("Replace…");
        replaceBtn.Invoke();
        Thread.Sleep(400);

        // Click "Replace with backups" (the Primary button on the dialog).
        var primary = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("Replace with backups")));
        Assert.That(primary, Is.Not.Null, "3-way dialog should expose 'Replace with backups'.");
        primary!.AsButton().Invoke();
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
