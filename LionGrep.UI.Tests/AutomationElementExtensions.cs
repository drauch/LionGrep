using FlaUI.Core.AutomationElements;
using FlaUI.Core.Exceptions;

namespace LionGrep.UI.Tests;

internal static class AutomationElementExtensions
{
    /// <summary>Like <see cref="AutomationElement.Name"/> but returns null when the underlying UIA
    /// peer doesn't support the Name property. Reading <c>Name</c> directly on such elements
    /// (common in some WinUI 3 visual-tree pieces) throws <see cref="PropertyNotSupportedException"/>;
    /// most test code wants a "no match" outcome instead.</summary>
    public static string? TryGetName(this AutomationElement element)
    {
        try { return element.Name; }
        catch (PropertyNotSupportedException) { return null; }
    }
}
