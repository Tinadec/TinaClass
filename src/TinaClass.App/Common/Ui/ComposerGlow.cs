using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace TinaClass.App.Common.Ui;

/// <summary>
/// The gradient glow around the composer while an answer is being made (design 11, "streaming": the input's outline gets a looping gradient).
/// Hand-rolled: Arc 1.1.0's <c>AiComposer</c> and <c>AiChat</c> only mark the composer <c>:generating</c> / <c>:busy</c> and draw no glow; the surface
/// of the composer (<c>PART_Surface</c>) is given a conic gradient outline as a local value while the host is busy, and it is cleared afterwards. A soft shadow in the middle color goes with it.
/// At Full motion the gradient turns round once in three seconds; at Reduced it stays still; at Off there is no glow (the state is still shown by the
/// stop button). The three colors are the design's glow colors (a light and a dark set), not Arc tokens: Arc has no gradient token.
/// </summary>
public static class ComposerGlow
{
    private static readonly Color[] s_light = [Color.Parse("#6A55F5"), Color.Parse("#1F9FE6"), Color.Parse("#E866B0")];
    private static readonly Color[] s_dark = [Color.Parse("#8B7BFF"), Color.Parse("#5CC8FF"), Color.Parse("#FF9AD5")];

    public static readonly AttachedProperty<string?> FollowsProperty = AvaloniaProperty.RegisterAttached<Control, Control, string?>("Follows");

    private static readonly AttachedProperty<Running?> s_runningProperty = AvaloniaProperty.RegisterAttached<Control, Control, Running?>("Running");

    static ComposerGlow() => FollowsProperty.Changed.AddClassHandler<Control>(OnFollowsChanged);

    public static string? GetFollows(Control control) => control.GetValue(FollowsProperty);

    /// <summary>The pseudo-class of the host that means "an answer is being made" (<c>:busy</c> on <c>AiChat</c>, <c>:generating</c> on <c>AiComposer</c>).</summary>
    public static void SetFollows(Control control, string? value) => control.SetValue(FollowsProperty, value);

    private static void OnFollowsChanged(Control host, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string pseudo || pseudo.Length == 0)
        {
            return;
        }
        host.Classes.CollectionChanged += (_, _) => Update(host, pseudo);
        Update(host, pseudo);
    }

    /// <summary>Starts or stops the glow of <paramref name="host"/> to match its pseudo-class.</summary>
    public static void Update(Control host, string pseudo)
    {
        var on = host.Classes.Contains(pseudo);
        var running = host.GetValue(s_runningProperty);
        if (!on)
        {
            running?.Stop();
            host.SetValue(s_runningProperty, null);
            return;
        }
        var level = ArcMotion.GetEffectiveLevel(host);
        if (running is not null || level == ArcMotionLevel.Off)
        {
            return;
        }
        var surface = host.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_Surface");
        if (surface is null)
        {
            return;
        }
        running = new Running(surface, host.ActualThemeVariant == ThemeVariant.Dark ? s_dark : s_light, level == ArcMotionLevel.Full);
        host.SetValue(s_runningProperty, running);
    }

    /// <summary>Whether the surface of <paramref name="host"/>'s composer shows the glow now (for tests).</summary>
    public static bool IsGlowing(Control host) => host.GetValue(s_runningProperty) is not null;

    private sealed class Running
    {
        private readonly Border _surface;
        private readonly CancellationTokenSource _cancel = new();

        public Running(Border surface, Color[] colors, bool turns)
        {
            _surface = surface;
            var brush = new ConicGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(colors[0], 0),
                    new GradientStop(colors[1], 0.33),
                    new GradientStop(colors[2], 0.66),
                    new GradientStop(colors[0], 1),
                ],
            };
            surface.BorderBrush = brush;
            surface.BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 28, Color = Color.FromArgb(0x55, colors[1].R, colors[1].G, colors[1].B) });
            if (turns)
            {
                var turn = new Animation
                {
                    Duration = TimeSpan.FromSeconds(3),
                    IterationCount = IterationCount.Infinite,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ConicGradientBrush.AngleProperty, 0.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ConicGradientBrush.AngleProperty, 360.0) } },
                    },
                };
                _ = turn.RunAsync(brush, _cancel.Token);
            }
        }

        public void Stop()
        {
            _cancel.Cancel();
            _surface.ClearValue(Border.BorderBrushProperty);
            _surface.ClearValue(Border.BoxShadowProperty);
        }
    }
}
