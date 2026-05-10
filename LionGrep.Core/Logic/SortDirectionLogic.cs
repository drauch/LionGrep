namespace LionGrep.Core.Logic;

public enum SortDirection { None, Ascending, Descending }

/// <summary>
/// Pure helpers for the column-header sort cycle and arrow rendering. Lets unit tests pin down
/// the cycle (None → Asc → Desc → None) and the "▲ / ▼" rendering without a live WinUI ListView.
/// </summary>
public static class SortDirectionLogic
{
    /// <summary>Cycles through the three states the way clicking a sortable header does.</summary>
    public static SortDirection ToggleNext(SortDirection current) => current switch
    {
        SortDirection.None       => SortDirection.Ascending,
        SortDirection.Ascending  => SortDirection.Descending,
        _                        => SortDirection.None,
    };

    /// <summary>Returns the column header label, suffixed with an arrow when the column is the active sort key.</summary>
    public static string FormatHeader(string display, bool isActiveSort, SortDirection direction)
    {
        if (!isActiveSort) return display;
        return direction switch
        {
            SortDirection.Ascending  => $"{display} ▲",   // ▲
            SortDirection.Descending => $"{display} ▼",   // ▼
            _                        => display,
        };
    }
}
