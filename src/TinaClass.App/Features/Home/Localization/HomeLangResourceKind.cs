using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Home.Localization;

/// <summary>The text of the Home feature (one member per string; the .xlf files next to this one hold en-US, zh-CN and zh-TW).</summary>
[LanguageCatalog]
public enum HomeLangResourceKind
{
    NavHome,
    GreetingMorning,
    GreetingAfternoon,
    GreetingEvening,
    Lead,
    Placeholder,
    Recent,
    Spaces,
    NoRecent,
    LoadError,
    Retry,
    Chats,
}
