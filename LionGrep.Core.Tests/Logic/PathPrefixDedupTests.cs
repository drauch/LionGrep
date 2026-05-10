using LionGrep.Core.Logic;
using NUnit.Framework;

namespace LionGrep.Core.Tests.Logic;

public class PathPrefixDedupTests
{
    [Test]
    public void EmptyOrSingleElement_PassesThrough()
    {
        Assert.That(PathPrefixDedup.Remove([]), Is.Empty);
        Assert.That(PathPrefixDedup.Remove([@"C:\Foo"]), Is.EqualTo(new[] { @"C:\Foo" }));
    }

    [Test]
    public void DescendantIsDroppedWhenAncestorPresent()
    {
        var result = PathPrefixDedup.Remove([@"C:\Foo", @"C:\Foo\Bar"]);
        Assert.That(result, Is.EqualTo(new[] { @"C:\Foo" }));
    }

    [Test]
    public void DescendantBeforeAncestor_AncestorWins_DescendantDropped()
    {
        var result = PathPrefixDedup.Remove([@"C:\Foo\Bar", @"C:\Foo"]);
        Assert.That(result, Is.EqualTo(new[] { @"C:\Foo" }));
    }

    [Test]
    public void Siblings_AreBothKept()
    {
        var result = PathPrefixDedup.Remove([@"C:\Foo", @"C:\Bar"]);
        Assert.That(result, Is.EqualTo(new[] { @"C:\Foo", @"C:\Bar" }));
    }

    [Test]
    public void ExactDuplicates_FirstWins()
    {
        var result = PathPrefixDedup.Remove([@"C:\Foo", @"C:\Foo"]);
        Assert.That(result, Is.EqualTo(new[] { @"C:\Foo" }));
    }

    [Test]
    public void TrailingSlashVariants_TreatedAsExactDuplicate()
    {
        var result = PathPrefixDedup.Remove([@"C:\Foo", @"C:\Foo\"]);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(@"C:\Foo"));
    }

    [Test]
    public void NameSubstring_IsNotTreatedAsDescendant()
    {
        // "C:\Foo" is NOT a prefix of "C:\FooBar" at directory-boundary level, even though it is
        // at character level. The trailing separator in the normalised form ensures we only
        // dedup actual subdirectories.
        var result = PathPrefixDedup.Remove([@"C:\Foo", @"C:\FooBar"]);
        Assert.That(result, Is.EqualTo(new[] { @"C:\Foo", @"C:\FooBar" }));
    }

    [Test]
    public void DeepDescendantThroughIntermediate_StillDropped()
    {
        var result = PathPrefixDedup.Remove([
            @"C:\A",
            @"C:\A\B\C\D\file-tree-root",
        ]);
        Assert.That(result, Is.EqualTo(new[] { @"C:\A" }));
    }

    [Test]
    [Platform("Win")]
    public void PathComparison_IsCaseInsensitiveOnWindows()
    {
        var result = PathPrefixDedup.Remove([@"C:\Foo", @"c:\foo\Bar"]);
        // The mixed-case sub-path should still be detected as a descendant on NTFS.
        Assert.That(result, Is.EqualTo(new[] { @"C:\Foo" }));
    }

    [Test]
    public void UnresolvablePath_IsSkipped_OthersUnaffected()
    {
        // GetFullPath("") throws ArgumentException; a string of just whitespace is filtered out
        // earlier by the IsNullOrWhiteSpace check. The valid path still surfaces.
        var result = PathPrefixDedup.Remove([@"  ", @"C:\Foo"]);
        Assert.That(result, Is.EqualTo(new[] { @"C:\Foo" }));
    }
}
