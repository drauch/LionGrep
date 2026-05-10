using NUnit.Framework;

namespace LionGrep.Core.Tests;

internal class CasePreserverTests
{
    [TestCase("foo", CaseClass.AllLower)]
    [TestCase("FOO", CaseClass.AllUpper)]
    [TestCase("Foo", CaseClass.TitleCase)]
    [TestCase("FoO", CaseClass.Mixed)]
    [TestCase("fOo", CaseClass.Mixed)]
    [TestCase("123", CaseClass.NoLetters)]
    [TestCase("", CaseClass.NoLetters)]
    [TestCase("a", CaseClass.AllLower)]
    [TestCase("A", CaseClass.AllUpper)]
    [TestCase("FooBar", CaseClass.Mixed)]
    public void Classify_KnownCasings(string input, CaseClass expected)
    {
        Assert.That(CasePreserver.Classify(input), Is.EqualTo(expected));
    }

    [Test]
    public void Apply_AllLower_LowercasesReplacement()
    {
        Assert.That(CasePreserver.Apply("BAR", "foo"), Is.EqualTo("bar"));
    }

    [Test]
    public void Apply_AllUpper_UppercasesReplacement()
    {
        Assert.That(CasePreserver.Apply("bar", "FOO"), Is.EqualTo("BAR"));
    }

    [Test]
    public void Apply_TitleCase_FirstUpperRestLower()
    {
        Assert.That(CasePreserver.Apply("BAR", "Foo"), Is.EqualTo("Bar"));
        Assert.That(CasePreserver.Apply("bar", "Foo"), Is.EqualTo("Bar"));
    }

    [Test]
    public void Apply_Mixed_ReturnsAsIs()
    {
        Assert.That(CasePreserver.Apply("BarBaz", "fOo"), Is.EqualTo("BarBaz"));
    }

    [Test]
    public void Apply_NoLetters_ReturnsAsIs()
    {
        Assert.That(CasePreserver.Apply("BarBaz", "123"), Is.EqualTo("BarBaz"));
    }

    [Test]
    public void Apply_EmptyReplacement_ReturnsEmpty()
    {
        Assert.That(CasePreserver.Apply("", "FOO"), Is.EqualTo(""));
    }
}
