using Avalonia.Controls;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Common.Navigation;

/// <summary>
/// One page of the app: a stable key (what the sidebar selects by), its name in the current language (a delegate, so a
/// feature with its own text catalog can supply it), its sidebar icon, how to create its view and how to reach its view model (the
/// shell reads the top bar's breadcrumb and actions from it). A page with <paramref name="InSidebar"/> false (Home) is reached by a
/// button, not from the list.
/// </summary>
public sealed record AppPage(string Key, Func<string> Title, LucideIconKind Icon, Func<Control> Create, Func<PageViewModel> Model, bool InSidebar = true)
{
    public string Label => Title();
}
