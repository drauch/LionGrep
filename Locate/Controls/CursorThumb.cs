using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Locate.Controls;

/// <summary>A column resize handle: a UserControl (gets the SizeWestEast cursor) wrapping a Thumb (handles drag).</summary>
public sealed partial class ColumnResizer : UserControl
{
    private readonly Thumb _thumb = new();

    public event EventHandler<DragDeltaEventArgs>? DragDelta;

    public ColumnResizer()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        _thumb.Background = new SolidColorBrush(Colors.Transparent);
        _thumb.HorizontalAlignment = HorizontalAlignment.Stretch;
        _thumb.VerticalAlignment = VerticalAlignment.Stretch;
        _thumb.DragDelta += (s, e) => DragDelta?.Invoke(this, e);

        Content = _thumb;
    }
}
