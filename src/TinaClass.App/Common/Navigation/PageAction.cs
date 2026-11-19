using System.Windows.Input;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Common.Navigation;

/// <summary>A button a page puts in the window's top bar (new knowledge base, publish, run). The label is a delegate so it follows the language.</summary>
public sealed record PageAction(Func<string> Text, ICommand Command, LucideIconKind? Icon = null, bool IsPrimary = false, bool IconOnly = false)
{
    public string Label => Text();

    public bool HasIcon => Icon is not null;

    public bool ShowsText => !IconOnly;

    public LucideIconKind IconKind => Icon ?? LucideIconKind.Plus;
}
