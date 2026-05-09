using Locate.Core.Logic;
using NUnit.Framework;

namespace Locate.Core.Tests.Logic;

public class CsvBuilderTests
{
    [Test]
    public void EmptyInput_StillEmitsHeader()
    {
        var csv = CsvBuilder.Build([]);
        Assert.That(csv.Trim(), Is.EqualTo("Name,Path,Line,Column,Text"));
    }

    [Test]
    public void FileWithoutLines_EmitsBlankLineColumnAndText()
    {
        var csv = CsvBuilder.Build([new CsvBuilder.FileEntry("a.txt", @"C:\a.txt", [])]);
        var lines = csv.TrimEnd('\r', '\n').Split('\n');
        Assert.That(lines, Has.Length.EqualTo(2));   // header + 1 row
        Assert.That(lines[1].TrimEnd('\r'), Is.EqualTo(@"a.txt,C:\a.txt,,,"));
    }

    [Test]
    public void OneFileWithTwoLines_OneRowPerLine()
    {
        var csv = CsvBuilder.Build([
            new CsvBuilder.FileEntry("a.txt", @"C:\a.txt", [
                new CsvBuilder.LineEntry(LineNumber: 5, Column: 3, Text: "hello"),
                new CsvBuilder.LineEntry(LineNumber: 9, Column: 1, Text: "world"),
            ])
        ]);
        var lines = csv.TrimEnd('\r', '\n').Split('\n');
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[1].TrimEnd('\r'), Is.EqualTo(@"a.txt,C:\a.txt,5,3,hello"));
        Assert.That(lines[2].TrimEnd('\r'), Is.EqualTo(@"a.txt,C:\a.txt,9,1,world"));
    }

    [TestCase("plain",            ExpectedResult = "plain")]
    [TestCase("with,comma",       ExpectedResult = "\"with,comma\"")]
    [TestCase("with\"quote",      ExpectedResult = "\"with\"\"quote\"")]
    [TestCase("with\rcr",         ExpectedResult = "\"with\rcr\"")]
    [TestCase("with\nlf",         ExpectedResult = "\"with\nlf\"")]
    [TestCase("",                 ExpectedResult = "")]
    public string Escape_FollowsRfc4180(string input) => CsvBuilder.Escape(input);

    [Test]
    public void Escape_NullReturnsEmptyString()
    {
        Assert.That(CsvBuilder.Escape(null), Is.EqualTo(""));
    }

    [Test]
    public void TextContainingDelimitersAndQuotes_RoundTripsWithProperEscaping()
    {
        var csv = CsvBuilder.Build([
            new CsvBuilder.FileEntry("a,b.txt", @"C:\a""b.txt", [
                new CsvBuilder.LineEntry(1, 1, "say \"hi\", world\n"),
            ])
        ]);
        // The whole row should still be parseable: each field escaped exactly once.
        Assert.That(csv, Does.Contain("\"a,b.txt\""));
        Assert.That(csv, Does.Contain("\"C:\\a\"\"b.txt\""));
        Assert.That(csv, Does.Contain("\"say \"\"hi\"\", world\n\""));
    }
}
