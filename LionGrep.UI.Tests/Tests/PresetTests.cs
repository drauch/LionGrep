using NUnit.Framework;

namespace LionGrep.UI.Tests.Tests;

[TestFixture]
[NonParallelizable]
public class PresetTests
{
    private AppDriver _driver = null!;

    [SetUp]
    public void Setup()
    {
        _driver = new AppDriver();
        _driver.ResetForm();
    }

    [Test]
    public void SaveAsPreset_ThenApply_RestoresWhatGroup()
    {
        // Set distinctive values, save as a preset, change form, apply preset, verify restoration.
        _driver.SetText("SearchPatternBox", "DistinctTokenForPresetTest");
        _driver.SetCheck("Use regex", true);
        _driver.SetCheck("Case sensitive", true);

        InvokeSavePreset("AutoTest_What");

        // Now change the form to something else.
        _driver.SetText("SearchPatternBox", "OtherPattern");
        _driver.SetCheck("Use regex", false);
        _driver.SetCheck("Case sensitive", false);

        // Apply the preset.
        ApplyPreset("AutoTest_What");

        Assert.That(_driver.GetText("SearchPatternBox"), Is.EqualTo("DistinctTokenForPresetTest"));
        Assert.That(_driver.CheckBoxByContent("Use regex").IsChecked, Is.True);
        Assert.That(_driver.CheckBoxByContent("Case sensitive").IsChecked, Is.True);
    }

    [Test]
    public void BetweenSizeFilter_SurvivesPresetRoundTrip_R1()
    {
        // R1 regression: SizeKbUpper used to be silently lost on preset save/restore. The R1
        // commit added the field; this test pins it down end-to-end.
        SelectSizeBetween();
        SetSizeValue(lower: 200, upper: 500);
        InvokeSavePreset("AutoTest_BetweenSize");

        // Move to a different size mode so the upper-bound NumberBox is hidden, then back to Between.
        SelectSizeMode(0);   // "All sizes"
        Thread.Sleep(150);

        ApplyPreset("AutoTest_BetweenSize");
        Thread.Sleep(300);

        var lower = ReadSizeValue(isUpper: false);
        var upper = ReadSizeValue(isUpper: true);
        Assert.That(lower, Is.EqualTo(200), "Lower bound (SizeKb) should round-trip through the preset.");
        Assert.That(upper, Is.EqualTo(500), "Upper bound (SizeKbUpper) is the R1 regression target — must round-trip.");
    }

    // ---- Helpers below — keep tests readable. ----

    private static void InvokeSavePreset(string name)
    {
        // Open the Presets dropdown.
        var presetsBtn = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("PresetsButton"));
        Assert.That(presetsBtn, Is.Not.Null, "PresetsButton not found.");
        presetsBtn!.AsButton().Invoke();
        Thread.Sleep(250);

        var saveItem = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem)
                .And(AppFixture.Automation.ConditionFactory.ByName("Save current as preset…")));
        Assert.That(saveItem, Is.Not.Null, "'Save current as preset…' menu item not found.");
        saveItem!.AsMenuItem().Invoke();
        Thread.Sleep(400);

        // The save dialog has a TextBox; the placeholder is "Preset name".
        var dialogTextBox = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
        Assert.That(dialogTextBox, Is.Not.Null, "ContentDialog TextBox not found.");
        var tb = dialogTextBox!.AsTextBox();
        tb.Focus();
        tb.Text = name;

        // Click Save (Primary button).
        var save = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Button)
                .And(AppFixture.Automation.ConditionFactory.ByName("Save")));
        Assert.That(save, Is.Not.Null);
        save!.AsButton().Invoke();
        Thread.Sleep(400);
    }

    private static void ApplyPreset(string name)
    {
        var presetsBtn = AppFixture.MainWindow.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByAutomationId("PresetsButton"));
        presetsBtn!.AsButton().Invoke();
        Thread.Sleep(250);

        AutomationElement? presetItem = null;
        foreach (var item in AppFixture.MainWindow.FindAllDescendants(
            AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem)))
        {
            if ((item.TryGetName() ?? "").StartsWith(name, StringComparison.Ordinal))
            { presetItem = item; break; }
        }
        Assert.That(presetItem, Is.Not.Null, $"Preset menu item starting with '{name}' not found.");
        presetItem!.AsMenuItem().Invoke();
        Thread.Sleep(300);
    }

    private static void SelectSizeBetween() => SelectSizeMode(3);

    private static void SelectSizeMode(int index)
    {
        var combos = AppFixture.MainWindow.FindAllDescendants(
            AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.ComboBox));
        Assert.That(combos.Length, Is.GreaterThanOrEqualTo(1), "Could not find Size ComboBox.");
        // Size is the first ComboBox in the form (Date is second). Selecting by index keeps this terse.
        combos[0].AsComboBox().Select(index);
        Thread.Sleep(150);
    }

    private static void SetSizeValue(int lower, int upper)
    {
        // Two NumberBoxes appear when "Between" is selected. NumberBox is recognised as Edit in UIA.
        var numberBoxes = AppFixture.MainWindow.FindAllDescendants(
            AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Spinner));
        if (numberBoxes.Length < 2)
        {
            // Fallback — some platforms expose NumberBox as Edit.
            numberBoxes = AppFixture.MainWindow.FindAllDescendants(
                AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
            // Filter to the two within the Size row by looking for the NumberBox-internal "InputBox" name.
            numberBoxes = numberBoxes.Where(e => (e.AutomationId ?? "").Contains("InputBox", StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        Assert.That(numberBoxes.Length, Is.GreaterThanOrEqualTo(2),
            "Expected two NumberBoxes for Between size mode. Found: " + numberBoxes.Length);
        TypeIntoNumberBox(numberBoxes[0], lower);
        TypeIntoNumberBox(numberBoxes[1], upper);
    }

    private static int ReadSizeValue(bool isUpper)
    {
        // NumberBox exposes itself as ControlType=Spinner with an inner Edit child holding the
        // actual value. AsTextBox().Text on the Spinner throws MethodNotSupportedException, so
        // always reach for the Edit descendant.
        var spinners = AppFixture.MainWindow.FindAllDescendants(
            AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Spinner));
        AutomationElement target;
        if (spinners.Length >= 2)
        {
            var spinnerEdit = spinners[isUpper ? 1 : 0].FindFirstDescendant(
                AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
            Assert.That(spinnerEdit, Is.Not.Null, "Spinner missing its inner Edit child.");
            target = spinnerEdit!;
        }
        else
        {
            var edits = AppFixture.MainWindow.FindAllDescendants(
                AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Edit))
                .Where(e => (e.AutomationId ?? "").Contains("InputBox", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            target = edits[isUpper ? 1 : 0];
        }
        var text = target.AsTextBox().Text ?? "";
        return int.TryParse(text, out var v) ? v : -1;
    }

    private static void TypeIntoNumberBox(AutomationElement nb, int value)
    {
        // NumberBox in WinUI 3 is a Spinner control type. Setting AsTextBox().Text="" on the
        // outer Spinner doesn't actually clear the inner Edit's content (it appears to no-op
        // silently), so typing a new value gets appended to the existing one ("256" + "200"
        // → "256200" or similar). Reach for the inner Edit and clear via Ctrl+A + Delete.
        var inner = nb.FindFirstDescendant(
            AppFixture.Automation.ConditionFactory.ByControlType(FlaUI.Core.Definitions.ControlType.Edit))
            ?? nb;
        inner.Focus();
        Thread.Sleep(50);
        FlaUI.Core.Input.Keyboard.TypeSimultaneously(
            FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
            FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
        Thread.Sleep(30);
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DELETE);
        Thread.Sleep(30);
        inner.AsTextBox().Enter(value.ToString());
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB); // commit
        Thread.Sleep(100);
    }
}
