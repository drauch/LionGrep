using LionGrep.Core.Logic;
using NUnit.Framework;

namespace LionGrep.Core.Tests.Logic;

public class ResponsiveLayoutTests
{
    [TestCase(0,    ExpectedResult = 0)]
    [TestCase(599,  ExpectedResult = 0)]
    [TestCase(600,  ExpectedResult = 1)]
    [TestCase(699,  ExpectedResult = 1)]
    [TestCase(700,  ExpectedResult = 2)]
    [TestCase(749,  ExpectedResult = 2)]
    [TestCase(750,  ExpectedResult = 3)]
    [TestCase(819,  ExpectedResult = 3)]
    [TestCase(820,  ExpectedResult = 4)]
    [TestCase(899,  ExpectedResult = 4)]
    [TestCase(900,  ExpectedResult = 5)]
    [TestCase(1049, ExpectedResult = 5)]
    [TestCase(1050, ExpectedResult = 6)]
    [TestCase(4096, ExpectedResult = 6)]
    public int Breakpoint_BoundariesPinned(double width) => ResponsiveLayout.GetBreakpoint(width);

    [Test]
    public void Bp0_ShowsOnlyName_StacksSizeDate()
    {
        var w = ResponsiveLayout.GetColumnWidths(0);
        Assert.That(w.Name,        Is.Positive);
        Assert.That(w.Matches,     Is.Zero);
        Assert.That(w.Ext,         Is.Zero);
        Assert.That(w.Encoding,    Is.Zero);
        Assert.That(w.Size,        Is.Zero);
        Assert.That(w.PathStretch, Is.False);
        Assert.That(w.Date,        Is.Zero);
        Assert.That(w.StackSizeDateBelowFileName, Is.True);
    }

    [Test]
    public void HideOrder_IsDate_Path_Size_Encoding_Ext_Matches()
    {
        // As we widen the window from bp1 → bp6, columns reappear in the documented priority.
        Assert.That(ResponsiveLayout.GetColumnWidths(1).Matches,     Is.Positive, "Matches reappears at bp1");
        Assert.That(ResponsiveLayout.GetColumnWidths(2).Ext,         Is.Positive, "Ext reappears at bp2");
        Assert.That(ResponsiveLayout.GetColumnWidths(3).Encoding,    Is.Positive, "Encoding reappears at bp3");
        Assert.That(ResponsiveLayout.GetColumnWidths(4).Size,        Is.Positive, "Size reappears at bp4");
        Assert.That(ResponsiveLayout.GetColumnWidths(5).PathStretch, Is.True,     "Path reappears (stretched) at bp5");
        Assert.That(ResponsiveLayout.GetColumnWidths(6).Date,        Is.Positive, "Date — last to reappear — at bp6");
    }

    [Test]
    public void NameAlwaysVisible_AcrossAllBreakpoints()
    {
        for (var bp = 0; bp <= ResponsiveLayout.MaxBreakpoint; bp++)
            Assert.That(ResponsiveLayout.GetColumnWidths(bp).Name, Is.Positive, $"bp={bp} should still show Name");
    }

    [TestCase(-5)]
    [TestCase(99)]
    public void OutOfRangeBreakpoint_IsClamped(int bp)
    {
        // Out-of-range inputs clamp to [0, MaxBreakpoint] — defensive for misuse.
        var w = ResponsiveLayout.GetColumnWidths(bp);
        Assert.That(w.Name, Is.Positive);
    }
}
