using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace TinaClass.App.Common.Theme;

/// <summary>
/// Switches the appearance with a crossfade: the window is captured as it is, the capture covers the window while the new
/// colors are applied underneath, then fades away on Arc's standard curve (an Arc <see cref="MotionValue{T}"/>). It is an
/// opacity change, so it also runs at reduced motion; with motion off the colors change at once.
/// </summary>
public static class ThemeTransition
{
    private static readonly TimeSpan s_duration = TimeSpan.FromMilliseconds(260);
    private static Window? s_window;

    /// <summary>The window that crossfades (the main window registers itself when it opens).</summary>
    public static void Register(Window window)
    {
        s_window = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(s_window, window))
            {
                s_window = null;
            }
        };
    }

    /// <summary>Changes the theme (appearance or anything else) with the crossfade when the window can show it.</summary>
    public static void Apply(Action<ArcThemeBuilder> configure)
    {
        var window = s_window;
        if (window is null || !window.IsVisible || ArcMotion.GetEffectiveLevel(window) == ArcMotionLevel.Off
            || OverlayLayer.GetOverlayLayer(window) is not { } layer || window.Bounds.Width < 1)
        {
            ArcTheme.Apply(configure);
            return;
        }

        var scale = window.RenderScaling;
        var size = window.Bounds.Size;
        var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        var cover = new Image { Source = bitmap, Width = size.Width, Height = size.Height, IsHitTestVisible = false };
        layer.Children.Add(cover);

        ArcTheme.Apply(configure);

        var fade = new MotionValue<double>(cover, 1, MotionVectors.Double);
        fade.Changed += value => cover.Opacity = value;
        _ = fade.AnimateTo(0, MotionSpec.Tween(s_duration, ArcEasings.Standard))
            .ContinueWith(_ => Remove(), TaskScheduler.FromCurrentSynchronizationContext());
        // A frame clock that stops (the window hidden mid-fade) must not leave the capture on top.
        DispatcherTimer.RunOnce(Remove, s_duration + TimeSpan.FromMilliseconds(400));

        void Remove()
        {
            if (cover.Source is null)
            {
                return;
            }
            fade.Stop();
            layer.Children.Remove(cover);
            cover.Source = null;
            bitmap.Dispose();
        }
    }
}
