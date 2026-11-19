using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TinaClass.App.Common.Ui;

/// <summary>One step of a run in the <see cref="RunTrace"/>: its name, how long it took, its state, and how wide it is drawn (the time it took, at least a little).</summary>
public sealed record TraceSegment(string Title, string Time, StatusTone Tone, double Weight, bool IsHollow = false);

/// <summary>
/// A horizontal strip of a run's steps, each as wide as it took, with a bar in the state's color, the step's name and its time under it (the workflow page's
/// "run trace", design 8). Hand-rolled: Arc's <c>AgentTimeline</c> is a playable multi-agent timeline with scrubbing and handoffs, and <c>Timeline</c> is a
/// vertical list of events; this is a plain, non-interactive strip (see "UI components" in docs/ARCHITECTURE.md). The bar is a <see cref="StatusDot"/> stretched
/// to the segment, so the state colors and the pulse of a running step come from the one place. The strip is rebuilt when <see cref="Segments"/> is set.
/// </summary>
public sealed class RunTrace : ContentControl
{
    public static readonly StyledProperty<IReadOnlyList<TraceSegment>?> SegmentsProperty = AvaloniaProperty.Register<RunTrace, IReadOnlyList<TraceSegment>?>(nameof(Segments));

    static RunTrace()
    {
        SegmentsProperty.Changed.AddClassHandler<RunTrace>((trace, _) => trace.Rebuild());
    }

    public IReadOnlyList<TraceSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ContentControl);

    private void Rebuild()
    {
        var segments = Segments ?? [];
        var grid = new Grid { ColumnSpacing = 4 };
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(segment.Weight, 1), GridUnitType.Star) { MinWidth = 72 });
            var bar = new StatusDot { Tone = segment.Tone, IsHollow = segment.IsHollow, Height = 4, Width = double.NaN, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Stretch };
            var title = new TextBlock { Text = segment.Title, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            title.Classes.Add("small");
            var time = new TextBlock { Text = segment.Time };
            time.Classes.Add("muted");
            time.Classes.Add("small");
            time.Classes.Add("mono");
            ToolTip.SetTip(title, segment.Title + (segment.Time.Length > 0 ? " · " + segment.Time : string.Empty));
            var column = new StackPanel { Spacing = 4, Children = { bar, title, time } };
            Grid.SetColumn(column, i);
            grid.Children.Add(column);
        }
        Content = grid;
    }
}
