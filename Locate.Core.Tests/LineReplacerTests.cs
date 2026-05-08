using System.Text;
using NUnit.Framework;

namespace Locate.Core.Tests;

public class LineReplacerTests
{
    private static string Replace(SearchOptions search, string replacement, bool preserveCase, string line, out int count)
    {
        var replacer = LineReplacerFactory.Create(search, replacement, preserveCase);
        var sb = new StringBuilder();
        count = replacer.ReplaceLine(line, sb);
        return sb.ToString();
    }

    [Test]
    public void Literal_SingleMatch_Replaced()
    {
        var result = Replace(new SearchOptions { Pattern = "fox", CaseSensitive = true }, "cat", false, "the fox jumped", out var count);
        Assert.That(result, Is.EqualTo("the cat jumped"));
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void Literal_MultipleMatches_AllReplaced()
    {
        var result = Replace(new SearchOptions { Pattern = "x", CaseSensitive = true }, "Y", false, "axbxcx", out var count);
        Assert.That(result, Is.EqualTo("aYbYcY"));
        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public void Literal_NoMatches_LineUnchanged()
    {
        var result = Replace(new SearchOptions { Pattern = "z", CaseSensitive = true }, "Y", false, "abc", out var count);
        Assert.That(result, Is.EqualTo("abc"));
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void Literal_PreserveCase_Lower()
    {
        var result = Replace(new SearchOptions { Pattern = "foo" }, "bar", true, "foo", out _);
        Assert.That(result, Is.EqualTo("bar"));
    }

    [Test]
    public void Literal_PreserveCase_Upper()
    {
        var result = Replace(new SearchOptions { Pattern = "foo" }, "bar", true, "FOO", out _);
        Assert.That(result, Is.EqualTo("BAR"));
    }

    [Test]
    public void Literal_PreserveCase_Title()
    {
        var result = Replace(new SearchOptions { Pattern = "foo" }, "bar", true, "Foo", out _);
        Assert.That(result, Is.EqualTo("Bar"));
    }

    [Test]
    public void Literal_PreserveCase_Mixed_AsIs()
    {
        var result = Replace(new SearchOptions { Pattern = "foo" }, "bar", true, "fOo", out _);
        Assert.That(result, Is.EqualTo("bar"));
    }

    [Test]
    public void Literal_WholeWord_RespectsBoundaries()
    {
        var result = Replace(new SearchOptions { Pattern = "cat", WholeWord = true, CaseSensitive = true },
            "dog", false, "cat catalog cat", out var count);
        Assert.That(result, Is.EqualTo("dog catalog dog"));
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void Regex_Backref_Expanded()
    {
        var result = Replace(new SearchOptions { Pattern = @"id=(\d+)", UseRegex = true, CaseSensitive = true },
            "ID:$1", false, "log id=42 ok", out var count);
        Assert.That(result, Is.EqualTo("log ID:42 ok"));
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void Regex_PreserveCase_AppliesToExpansion()
    {
        var result = Replace(new SearchOptions { Pattern = @"(\w+)", UseRegex = true, CaseSensitive = true },
            "[$1]", true, "FOO", out _);
        Assert.That(result, Is.EqualTo("[FOO]"));
    }

    [Test]
    public void Regex_MultipleMatches_AllReplaced()
    {
        var result = Replace(new SearchOptions { Pattern = @"\d+", UseRegex = true, CaseSensitive = true },
            "N", false, "a1 b22 c333", out var count);
        Assert.That(result, Is.EqualTo("aN bN cN"));
        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public void EmptyReplacement_DeletesMatches()
    {
        var result = Replace(new SearchOptions { Pattern = "x", CaseSensitive = true }, "", false, "axbxc", out var count);
        Assert.That(result, Is.EqualTo("abc"));
        Assert.That(count, Is.EqualTo(2));
    }
}
