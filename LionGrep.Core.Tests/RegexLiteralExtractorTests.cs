using NUnit.Framework;

namespace LionGrep.Core.Tests;

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
    // R2 — patterns ending with {n} after a metachar used to crash with IndexOutOfRangeException.
    [TestCase(@"foo.{3}",          ExpectedResult = "foo")]
    [TestCase(@"foo${3}",          ExpectedResult = "foo")]
    [TestCase(@"foo|{3}",          ExpectedResult = "<none>")]   // top-level alternation kills the prefilter regardless
    [TestCase(@"foo}{3}",          ExpectedResult = "foo")]      // bare '}' from inside a class context
    // R3 — {n} (n>=2) on a literal char in the middle of a run breaks contiguity for everything
    // AFTER the quantified char. The run still keeps the quantified char itself (it appears at
    // least once in any match), so "foa{2}b" → "foa" (matches "foaab", which contains "foa" but
    // NOT "foab"). The previous extractor produced "foab" and silently dropped real matches.
    [TestCase(@"foa{2}b",          ExpectedResult = "foa")]
    [TestCase(@"foo\.{3}bar",      ExpectedResult = "foo.")]
    [TestCase(@"a{1}b",            ExpectedResult = "ab")]   // {1} is exactly-once; the run continues
    // R7 — escapes that don't represent literal chars used to be appended as if they did. \p{L}
    // matches any Unicode letter (NOT the byte sequence "p"+"L"+"}"), so the prefilter treating
    // the 'p' as a literal char was a silent missed-match bug.
    [TestCase(@"\p{L}foo",         ExpectedResult = "foo")]   // was returning "pfoo" (broken)
    [TestCase(@"\P{Lu}bar",        ExpectedResult = "bar")]   // negated category — same shape
    [TestCase(@"\xAA",             ExpectedResult = "<none>")] // hex escape — no useful literal at all
    [TestCase(@"foo\xAAbar",       ExpectedResult = "foo")]   // hex escape splits the run
    [TestCase(@"foo\cXbar",        ExpectedResult = "foo")]   // control-char escape splits the run
    // R14 — lazy quantifiers: ? * + each have a trailing-? lazy form. The atom-required-ness is
    // identical to the greedy form; we just have to skip past the trailing ?.
    [TestCase(@"FooBar+?",         ExpectedResult = "FooBar")]   // +? — atom required ≥ 1 time
    [TestCase(@"FooBar*?",         ExpectedResult = "FooBa")]    // *? — atom optional, drops 'r'
    [TestCase(@"Fooa??b",          ExpectedResult = "Foo")]      // ?? — 'a' optional; "Foo" (3) beats trailing "b" (1)
    // R14 — nested groups exercise SkipBalanced's depth counter.
    [TestCase(@"((alpha))bravo",   ExpectedResult = "bravo")]    // group skipped, then 5-char run wins
    [TestCase(@"prefix((a|b))suffix", ExpectedResult = "prefix")] // first run wins on a length tie
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
