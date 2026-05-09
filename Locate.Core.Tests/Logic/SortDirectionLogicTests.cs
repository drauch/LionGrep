using Locate.Core.Logic;
using NUnit.Framework;

namespace Locate.Core.Tests.Logic;

public class SortDirectionLogicTests
{
    [Test]
    public void ToggleNext_CyclesNoneAscDescNone()
    {
        var s = SortDirection.None;
        s = SortDirectionLogic.ToggleNext(s); Assert.That(s, Is.EqualTo(SortDirection.Ascending));
        s = SortDirectionLogic.ToggleNext(s); Assert.That(s, Is.EqualTo(SortDirection.Descending));
        s = SortDirectionLogic.ToggleNext(s); Assert.That(s, Is.EqualTo(SortDirection.None));
        s = SortDirectionLogic.ToggleNext(s); Assert.That(s, Is.EqualTo(SortDirection.Ascending));
    }

    [TestCase("Path", false, SortDirection.Ascending,  ExpectedResult = "Path")]
    [TestCase("Path", true,  SortDirection.Ascending,  ExpectedResult = "Path ▲")]   // ▲
    [TestCase("Path", true,  SortDirection.Descending, ExpectedResult = "Path ▼")]   // ▼
    [TestCase("Date modified", true, SortDirection.None, ExpectedResult = "Date modified")] // unchanged when no direction
    public string FormatHeader_AppendsArrowOnlyWhenActive(string display, bool isActive, SortDirection dir)
        => SortDirectionLogic.FormatHeader(display, isActive, dir);
}
