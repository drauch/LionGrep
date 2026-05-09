using NUnit.Framework;

namespace Locate.Core.Tests;

public class RegexLiteralExtractorTests
{
    [TestCase(@"Foo\d+",          ExpectedResult = "Foo")]
    [TestCase(@"\bclass\s+\w+",    ExpectedResult = "class")]
    [TestCase(@"class\s+(\w+)",    ExpectedResult = "class")]
    [TestCase(@"^foo$",            ExpectedResult = "foo")]
    [TestCase(@"(foo|bar)baz",     ExpectedResult = "baz")]
    [TestCase(@"Foo+",             ExpectedResult = "Foo")]
    [TestCase(@"Foo*",             ExpectedResult = "Fo")]
    [TestCase(@"[Ff]oo",           ExpectedResult = "oo")]
    [TestCase(@"Foo{0,5}",         ExpectedResult = "Fo")]
    [TestCase(@"Foo{3}",           ExpectedResult = "Foo")]
    [TestCase(@"Foo\d{3}\.txt",    ExpectedResult = ".txt")]
    [TestCase(@"abc\..+xyz",       ExpectedResult = "abc.")]
    [TestCase(@"\babcdef\b",       ExpectedResult = "abcdef")]
    [TestCase(@"foo\nbar",         ExpectedResult = "foo\nbar")]
    public string ExtractsRequiredLiteral(string pattern)
        => RegexLiteralExtractor.TryExtractRequiredLiteral(pattern) ?? "<none>";

    [TestCase(@"foo|bar")]                  // top-level alternation kills the prefilter
    [TestCase(@"^|bar")]
    [TestCase(@"a")]                        // single char literal — too short, < 2
    [TestCase(@"")]                          // empty
    [TestCase(@"\d+")]                       // no literal
    [TestCase(@"[abc][def]")]                // only character classes
    [TestCase(@"a?b?c?")]                    // every char is optional
    [TestCase(@"\d{3}-\d{4}")]               // only a single '-' literal, length < 2
    public void NoUsefulLiteral(string pattern)
        => Assert.That(RegexLiteralExtractor.TryExtractRequiredLiteral(pattern), Is.Null);
}
