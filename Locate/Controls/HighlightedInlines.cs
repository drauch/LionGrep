using System.Collections.Generic;
using Locate.Core.Logic;
using Locate.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Locate.Controls;

/// <summary>
/// Attached property that lets a <see cref="TextBlock"/> render an
/// <see cref="IReadOnlyList{TextSegment}"/> as a sequence of inlines, with engine-match (yellow)
/// and filter-match (blue) backgrounds applied to the relevant pieces. Used for the Directory
/// column where we want the same yellow/blue treatment as the file-name and matched-line cells
/// but also need <c>TextTrimming="CharacterEllipsis"</c> to survive long paths — that rules out
/// an <c>ItemsControl</c>-of-Borders, since trimming only works inside a single TextBlock.
///
/// <para>Plain segments become <see cref="Run"/> instances; highlighted segments become an
/// <see cref="InlineUIContainer"/> wrapping a <see cref="Border"/> with the appropriate background.
/// The TextBlock continues to perform its own ellipsis on the resulting flow.</para>
/// </summary>
public static class HighlightedInlines
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments",
        typeof(IReadOnlyList<TextSegment>),
        typeof(HighlightedInlines),
        new PropertyMetadata(null, OnSegmentsChanged));

    public static IReadOnlyList<TextSegment>? GetSegments(TextBlock obj) =>
        (IReadOnlyList<TextSegment>?)obj.GetValue(SegmentsProperty);

    public static void SetSegments(TextBlock obj, IReadOnlyList<TextSegment>? value) =>
        obj.SetValue(SegmentsProperty, value);

    // Color (the struct) lives in Windows.UI — Microsoft.UI only re-exports the predefined Colors values.
    private static readonly SolidColorBrush EngineBrush = new(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xEB, 0x3B));
    private static readonly SolidColorBrush FilterBrush = new(Windows.UI.Color.FromArgb(0xFF, 0x21, 0x96, 0xF3));
    private static readonly SolidColorBrush BlackBrush = new(Colors.Black);
    private static readonly SolidColorBrush WhiteBrush = new(Colors.White);

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;

        tb.Inlines.Clear();
        if (e.NewValue is not IReadOnlyList<TextSegment> segments) return;

        foreach (var seg in segments)
        {
            if (seg.Kind == HighlightKind.None)
            {
                tb.Inlines.Add(new Run { Text = seg.Text });
                continue;
            }

            var isEngine = seg.Kind == HighlightKind.EngineMatch;
            var inner = new TextBlock
            {
                Text = seg.Text,
                FontSize = tb.FontSize,
                FontFamily = tb.FontFamily,
                FontWeight = tb.FontWeight,
                Foreground = isEngine ? BlackBrush : WhiteBrush,
            };
            var border = new Border
            {
                Background = isEngine ? EngineBrush : FilterBrush,
                Padding = new Thickness(0),
                Child = inner,
            };
            tb.Inlines.Add(new InlineUIContainer { Child = border });
        }
    }
}
