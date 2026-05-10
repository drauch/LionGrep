namespace LionGrep.Core.Logic;

/// <summary>
/// Translates window width into the result-table column visibility/widths the UI should apply.
/// Pure function so the breakpoint table can be unit-tested without spinning up WinUI.
/// Column hide priority (most aggressive first): Date → Path → Size → Encoding → Ext → Matches.
/// Name is always visible.
/// </summary>
public static class ResponsiveLayout
{
    /// <summary><c>0</c> means hidden / collapsed; positive values are pixels;
    /// <see cref="PathStretch"/> indicates the path column should be star-sized rather than fixed.</summary>
    public readonly record struct ColumnWidthSet(
        int Name,
        int Size,
        int Matches,
        bool PathStretch,
        int Ext,
        int Encoding,
        int Date,
        bool StackSizeDateBelowFileName);

    public const int MaxBreakpoint = 6;

    public static int GetBreakpoint(double width) => width switch
    {
        < 600  => 0,
        < 700  => 1,
        < 750  => 2,
        < 820  => 3,
        < 900  => 4,
        < 1050 => 5,
        _      => 6,
    };

    public static ColumnWidthSet GetColumnWidths(int breakpoint)
    {
        if (breakpoint < 0) breakpoint = 0;
        if (breakpoint > MaxBreakpoint) breakpoint = MaxBreakpoint;

        return new ColumnWidthSet(
            Name:        240,
            Size:        breakpoint >= 4 ? 70  : 0,
            Matches:     breakpoint >= 1 ? 70  : 0,
            PathStretch: breakpoint >= 5,
            Ext:         breakpoint >= 2 ? 50  : 0,
            Encoding:    breakpoint >= 3 ? 80  : 0,
            Date:        breakpoint >= 6 ? 160 : 0,
            // At very narrow widths Size and Date stack underneath the file-name pair instead of beside it.
            StackSizeDateBelowFileName: breakpoint <= 1);
    }
}
