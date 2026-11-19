using Avalonia;
using Avalonia.Controls;
using TKCode.Arc.Motion;

namespace TinaClass.App.Common.Ui;

/// <summary>How a <see cref="StatusDot"/> looks: the status colors of the design (success, warning, danger), a muted gray, and a running dot that pulses.</summary>
public enum StatusTone
{
    Muted,
    Success,
    Warning,
    Danger,
    Running,
}

/// <summary>
/// An 8px dot for a state (a provider that answers, a run that waits). Hand-rolled: Arc's <c>Badge</c> is a text pill with no 8px dot form
/// (see "UI components" in docs/ARCHITECTURE.md). It is a <see cref="Border"/> whose color comes from its tone class in
/// Styles/Panels.axaml; the running dot pulses at Full motion only (it tracks the motion level through Arc's <c>:motion-full</c> pseudo-class).
/// <see cref="IsHollow"/> draws a ring, for "not set" or "not reached".
/// </summary>
public sealed class StatusDot : Border
{
    public static readonly StyledProperty<StatusTone> ToneProperty = AvaloniaProperty.Register<StatusDot, StatusTone>(nameof(Tone));

    public static readonly StyledProperty<bool> IsHollowProperty = AvaloniaProperty.Register<StatusDot, bool>(nameof(IsHollow));

    static StatusDot()
    {
        ToneProperty.Changed.AddClassHandler<StatusDot>((dot, _) => dot.UpdateClasses());
        IsHollowProperty.Changed.AddClassHandler<StatusDot>((dot, _) => dot.UpdateClasses());
    }

    public StatusDot()
    {
        ArcMotion.SetTracks(this, true);
        UpdateClasses();
    }

    public StatusTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public bool IsHollow
    {
        get => GetValue(IsHollowProperty);
        set => SetValue(IsHollowProperty, value);
    }

    private void UpdateClasses()
    {
        Classes.Set("success", Tone == StatusTone.Success);
        Classes.Set("warning", Tone == StatusTone.Warning);
        Classes.Set("danger", Tone == StatusTone.Danger);
        Classes.Set("muted", Tone == StatusTone.Muted);
        Classes.Set("running", Tone == StatusTone.Running);
        Classes.Set("hollow", IsHollow);
    }
}
