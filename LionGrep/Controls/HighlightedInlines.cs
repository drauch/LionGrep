using System.Collections.Generic;
using LionGrep.Core.Logic;
using LionGrep.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace LionGrep.Controls;

/// <summary>
/// Attached property that lets a <see cref="TextBlock"/> render an
/// <see cref="IReadOnlyList{TextSegment}"/> as a single line of text with engine-match (yellow)
/// and filter-match (blue) backgrounds applied to the relevant character ranges. Used for every
/// highlight-aware cell — file name, directory, and matched line.
///
/// <para>Segments become <see cref="Run"/> inlines (so engine matches can use SemiBold weight) plus
/// <see cref="TextHighlighter"/> ranges (for the colored backgrounds). This is the canonical WinUI
/// pattern: <see cref="InlineUIContainer"/>-wrapped Borders mid-flow break subsequent inline
/// layout on long lines — content past the first highlight stops rendering. TextHighlighters paint
/// over the existing text without disturbing the flow, so spacing and trailing content are preserved.</para>
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
        tb.TextHighlighters.Clear();
        if (e.NewValue is not IReadOnlyList<TextSegment> segments) return;

        var index = 0;
        foreach (var seg in segments)
        {
            var isEngine = seg.Kind == HighlightKind.EngineMatch;
            var run = new Run { Text = seg.Text };
            if (isEngine) run.FontWeight = FontWeights.SemiBold;
            tb.Inlines.Add(run);

            if (seg.Kind != HighlightKind.None)
            {
                var hl = new TextHighlighter
                {
                    Background = isEngine ? EngineBrush : FilterBrush,
                    Foreground = isEngine ? BlackBrush : WhiteBrush,
                };
                hl.Ranges.Add(new TextRange { StartIndex = index, Length = seg.Text.Length });
                tb.TextHighlighters.Add(hl);
            }
            index += seg.Text.Length;
        }
    }
}
