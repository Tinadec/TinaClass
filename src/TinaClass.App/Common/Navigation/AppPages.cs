using Avalonia.Controls;
using TinaClass.App.Common.Localization;
using TinaClass.App.Features.Chat;
using TinaClass.App.Features.Chat.Localization;
using TinaClass.App.Features.Home;
using TinaClass.App.Features.Home.Localization;
using TinaClass.App.Features.Models;
using TinaClass.App.Features.Models.Localization;
using TinaClass.App.Features.Knowledge;
using TinaClass.App.Features.Knowledge.Localization;
using TinaClass.App.Features.Agents;
using TinaClass.App.Features.Agents.Localization;
using TinaClass.App.Features.Flow;
using TinaClass.App.Features.Flow.Localization;
using TinaClass.App.Features.Prompts;
using TinaClass.App.Features.Prompts.Localization;
using TinaClass.App.Features.Team;
using TinaClass.App.Features.Team.Localization;
using TinaClass.App.Features.Usage;
using TinaClass.App.Features.Usage.Localization;
using TinaClass.App.Features.Settings;
using TinaClass.App.Features.Settings.Localization;
using Microsoft.Extensions.DependencyInjection;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Common.Navigation;

/// <summary>
/// The pages, in sidebar order. The sidebar and the shell's title read this list; each entry pairs a view with its view
/// model from the container (written out, no reflection). To add a page, add its feature folder and an entry here.
/// </summary>
public static class AppPages
{
    public const string Home = "home";
    public const string Chat = "chat";
    public const string Models = "models";
    public const string Knowledge = "knowledge";
    public const string Agents = "agents";
    public const string Flow = "flow";
    public const string Prompts = "prompts";
    public const string Team = "team";
    public const string Usage = "usage";
    public const string Settings = "settings";

    public static IReadOnlyList<AppPage> All { get; } =
    [
        new(Home, () => HomeLangResources.Get(HomeLangResourceKind.NavHome), LucideIconKind.House, () => View<HomePage, HomeViewModel>(), Model<HomeViewModel>, InSidebar: false),
        new(Chat, () => ChatLangResources.Get(ChatLangResourceKind.NavChat), LucideIconKind.MessageSquare, () => View<ChatPage, ChatViewModel>(), Model<ChatViewModel>),
        new(Knowledge, () => KnowledgeLangResources.Get(KnowledgeLangResourceKind.NavKnowledge), LucideIconKind.BookOpen, () => View<KnowledgePage, KnowledgeViewModel>(), Model<KnowledgeViewModel>),
        new(Agents, () => AgentsLangResources.Get(AgentsLangResourceKind.NavAgents), LucideIconKind.Bot, () => View<AgentsPage, AgentsViewModel>(), Model<AgentsViewModel>),
        new(Flow, () => FlowLangResources.Get(FlowLangResourceKind.NavFlow), LucideIconKind.Workflow, () => View<FlowPage, FlowViewModel>(), Model<FlowViewModel>),
        new(Prompts, () => PromptsLangResources.Get(PromptsLangResourceKind.NavPrompts), LucideIconKind.Sparkles, () => View<PromptsPage, PromptsViewModel>(), Model<PromptsViewModel>),
        new(Team, () => TeamLangResources.Get(TeamLangResourceKind.NavTeam), LucideIconKind.Users, () => View<TeamPage, TeamViewModel>(), Model<TeamViewModel>),
        new(Usage, () => UsageLangResources.Get(UsageLangResourceKind.NavUsage), LucideIconKind.ChartColumn, () => View<UsagePage, UsageViewModel>(), Model<UsageViewModel>),
        new(Models, () => ModelsLangResources.Get(ModelsLangResourceKind.NavModels), LucideIconKind.Cpu, () => View<ModelsPage, ModelsViewModel>(), Model<ModelsViewModel>),
        new(Settings, () => SettingsLangResources.Get(SettingsLangResourceKind.NavSettings), LucideIconKind.Settings, () => View<SettingsPage, SettingsViewModel>(), Model<SettingsViewModel>),
    ];

    public static AppPage? Find(string? key) => All.FirstOrDefault(page => page.Key == key);

    private static PageViewModel Model<TViewModel>() where TViewModel : PageViewModel => DesktopApp.Services.GetRequiredService<TViewModel>();

    private static TView View<TView, TViewModel>() where TView : Control, new() where TViewModel : notnull =>
        new() { DataContext = DesktopApp.Services.GetRequiredService<TViewModel>() };
}
