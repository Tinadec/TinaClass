using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace TinaClass.App.Common.Ui;

/// <summary>
/// The app's own motion, built on Arc's motion values and levels (<see cref="ArcMotion"/>): everything here jumps when the
/// effective level is Off, and only fades (no travel) when it is Reduced.
/// <list type="bullet">
/// <item><c>ui:Motion.FadeOn="{Binding Selected}"</c>: the control fades in again (rising 4px at full motion) when the value
/// changes from one item to another, so a figure visibly follows a change.</item>
/// <item><c>ui:Motion.HidesWhenClosed</c> (set on every <c>arc:Alert</c> by Styles/Panels.axaml): a closed alert leaves the
/// layout once its collapse finished, so it keeps no stack spacing, while the opening and closing still animate.</item>
/// </list>
/// </summary>
public static class Motion
{
    public static readonly AttachedProperty<object?> FadeOnProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, object?>("FadeOn");

    public static readonly AttachedProperty<bool> HidesWhenClosedProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("HidesWhenClosed");

    private static readonly TimeSpan s_fadeDuration = TimeSpan.FromMilliseconds(200);
    private static readonly ConditionalWeakTable<Control, Fader> s_faders = new();

    static Motion()
    {
        FadeOnProperty.Changed.AddClassHandler<Control>(OnFadeOnChanged);
        HidesWhenClosedProperty.Changed.AddClassHandler<Control>(OnHidesWhenClosedChanged);
    }

    public static object? GetFadeOn(Control control) => control.GetValue(FadeOnProperty);

    public static void SetFadeOn(Control control, object? value) => control.SetValue(FadeOnProperty, value);

    public static bool GetHidesWhenClosed(Control control) => control.GetValue(HidesWhenClosedProperty);

    public static void SetHidesWhenClosed(Control control, bool value) => control.SetValue(HidesWhenClosedProperty, value);

    private static void OnFadeOnChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        // Only a change from one value to another: the first value and clearing it are not a "next item".
        if (e.OldValue is null || e.NewValue is null || ReferenceEquals(e.OldValue, e.NewValue) || !control.IsLoaded)
        {
            return;
        }
        var level = ArcMotion.GetEffectiveLevel(control);
        if (level == ArcMotionLevel.Off)
        {
            return;
        }
        s_faders.GetValue(control, c => new Fader(c)).Play(level == ArcMotionLevel.Full);
    }

    private sealed class Fader
    {
        private readonly Control _control;
        private readonly MotionValue<double> _progress;
        private readonly TranslateTransform _rise = new();
        private bool _travels;

        public Fader(Control control)
        {
            _control = control;
            _progress = new MotionValue<double>(control, 1, MotionVectors.Double);
            _progress.Changed += OnStep;
        }

        public void Play(bool travels)
        {
            _travels = travels && _control.RenderTransform is null or TranslateTransform;
            if (_travels)
            {
                _control.RenderTransform = _rise;
            }
            _progress.Jump(0);
            _ = _progress.AnimateTo(1, MotionSpec.Tween(s_fadeDuration, ArcEasings.Standard));
        }

        private void OnStep(double p)
        {
            _control.Opacity = p;
            if (_travels)
            {
                _rise.Y = 4 * (1 - p);
            }
        }
    }

    private static void OnHidesWhenClosedChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (control is not Alert alert || e.NewValue is not true)
        {
            return;
        }
        alert.IsVisible = alert.IsOpen;
        alert.PropertyChanged += (_, change) =>
        {
            if (change.Property != Alert.IsOpenProperty)
            {
                return;
            }
            if (alert.IsOpen)
            {
                alert.IsVisible = true;
                return;
            }
            if (!alert.IsLoaded || ArcMotion.GetEffectiveLevel(alert) == ArcMotionLevel.Off)
            {
                alert.IsVisible = false;
                return;
            }
            // Leave the layout when the collapse is done (or after it at the latest).
            DispatcherTimer.RunOnce(() =>
            {
                if (!alert.IsOpen)
                {
                    alert.IsVisible = false;
                }
            }, TimeSpan.FromMilliseconds(520));
        };
    }
}
