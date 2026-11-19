using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TKCode.Arc;

namespace TinaClass.App.Common.Ui;

/// <summary>
/// A small key cap for a shortcut ("⌘K", "↵"). Hand-rolled: Arc has <c>ShortcutKeyCap</c> only as a struct that formats text, and
/// <c>Badge</c> is a proportional text pill (see "UI components" in docs/ARCHITECTURE.md). <see cref="Gesture"/> is written with
/// <c>Mod</c> for the platform's main key: "Mod+K" shows "⌘K" on macOS and "Ctrl+K" elsewhere. One Border and one TextBlock in
/// Geist Mono, the same size everywhere.
/// </summary>
public sealed class KeyCap : Border
{
    public static readonly StyledProperty<string?> GestureProperty = AvaloniaProperty.Register<KeyCap, string?>(nameof(Gesture));

    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };

    static KeyCap() => GestureProperty.Changed.AddClassHandler<KeyCap>((cap, _) => cap.UpdateText());

    public KeyCap()
    {
        Classes.Add("keycap");
        Child = _text;
    }

    public string? Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    /// <summary>The text for a gesture on this platform.</summary>
    public static string Format(string? gesture)
    {
        if (string.IsNullOrEmpty(gesture))
        {
            return string.Empty;
        }
        var mac = OperatingSystem.IsMacOS();
        var parts = gesture.Split('+');
        var text = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            text.Append(part switch
            {
                "Mod" => mac ? "⌘" : "Ctrl+",
                "Shift" => mac ? "⇧" : "Shift+",
                "Alt" => mac ? "⌥" : "Alt+",
                "Enter" => "↵",
                "Esc" => "Esc",
                _ => part,
            });
        }
        return text.ToString();
    }

    private void UpdateText() => _text.Text = Format(Gesture);
}
