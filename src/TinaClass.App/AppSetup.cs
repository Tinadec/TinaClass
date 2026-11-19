using Avalonia;
using TinaClass.App.Common;
using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Settings;
using TinaClass.App.Common.Theme;
using TKCode.Arc;
using TKCode.Arc.Fonts;
using TKCode.Arc.Themes.Corners;
using TinaClass.App.Common.Api.Localization;
using TinaClass.App.Features.Chat.Localization;
using TinaClass.App.Features.Home.Localization;
using TinaClass.App.Features.Models.Localization;
using TinaClass.App.Features.Knowledge.Localization;
using TinaClass.App.Features.Agents.Localization;
using TinaClass.App.Features.Flow.Localization;
using TinaClass.App.Features.Prompts.Localization;
using TinaClass.App.Features.Team.Localization;
using TinaClass.App.Features.Usage.Localization;

namespace TinaClass.App;

/// <summary>
/// How the app sets up Avalonia and Arc, in one place: the desktop (Program.cs) and the tests start from
/// <c>AppBuilder.Configure&lt;DesktopApp&gt;()</c>, pick their platform and call <see cref="UseDesktopSetup"/>.
/// </summary>
public static class AppSetup
{
    /// <summary>Fonts, Arc's packages, the app's text and the theme of the first frame.</summary>
    public static AppBuilder UseDesktopSetup(this AppBuilder builder)
    {
        Contracts.Common.Money.Configure(AppInfo.Current.Currency);
        return builder.WithInterFont()     // Inter for body text
               .WithArcFonts()      // Geist for display text, Geist Mono for numbers and code
               .UseArc(arc => arc
                   // The languages and the one to start in (Common/Localization/AppLanguages.cs); ArcLanguage.Set switches.
                   .UseLanguages(AppLanguages.Initial, AppLanguages.Supported)
                   .UseArcBlocks()   // Arc's controls and the page-level blocks (Sidebar, AiChat, CommandPaletteDialog)
                   .UseArcSpecial()  // the AI controls (AgentPlan, ContextWindow, ProviderKeys, ...)
                   .UseArcWorkflow() // the workflow canvas
                   .UseArcCorners()  // the corner styles arc.theme.json picks from
                   // The app's own text (Common/Localization/*.xlf), and each feature's.
                   .UsePackage(AppInfo.PackageId, static p => p.AddLanguages([
                       AppLangResources.Catalog,
                       ApiLangResources.Catalog,
                       HomeLangResources.Catalog,
                       ChatLangResources.Catalog,
                       ModelsLangResources.Catalog,
                       KnowledgeLangResources.Catalog,
                       AgentsLangResources.Catalog,
                       FlowLangResources.Catalog,
                       PromptsLangResources.Catalog,
                       TeamLangResources.Catalog,
                       UsageLangResources.Catalog,
                   ]))
                   // The theme of the first frame: arc.theme.json, then the appearance chosen in the app (if any).
                   .WithTheme(theme =>
                   {
                       ThemeFile.Current.ApplyTo(theme);
                       UserSettings.Current.ApplyAppearance(theme);
                   }));
    }
}
