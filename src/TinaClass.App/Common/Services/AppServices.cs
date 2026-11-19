using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Shell;
using TinaClass.App.Features.Chat;
using TinaClass.App.Features.Home;
using TinaClass.App.Features.Models;
using TinaClass.App.Features.Knowledge;
using TinaClass.App.Features.Agents;
using TinaClass.App.Features.Flow;
using TinaClass.App.Features.Prompts;
using TinaClass.App.Features.Team;
using TinaClass.App.Features.Usage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.App.Common.Api;
using TinaClass.App.Features.Chat.Api;
using TinaClass.App.Features.Models.Api;
using TinaClass.App.Features.Knowledge.Api;
using TinaClass.App.Features.Agents.Api;
using TinaClass.App.Features.Flow.Api;
using TinaClass.App.Features.Prompts.Api;
using TinaClass.App.Features.Team.Api;
using TinaClass.App.Features.Usage.Api;

namespace TinaClass.App.Common.Services;

/// <summary>
/// Every service of the app, in one place. Each feature's <c>Add…</c> (in its folder) registers its view models and its
/// services. With the API (<c>app.json</c> <c>"dataSource": "api"</c>) the API services replace the sample ones below, after the
/// features registered theirs. Everything is written out, so the container needs no reflection.
/// </summary>
public static class AppServices
{
    public static IServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<INavigator>(sp => sp.GetRequiredService<ShellViewModel>());
        // Cross-feature calls go through interfaces like these; a feature that is present replaces the default (the Usage feature provides the real spend).
        services.AddSingleton<ISpendInfo, NoSpendInfo>();

        // Core features
        services.AddModels();
        services.AddChat();
        services.AddHome();
        // Optional features: each is a switch in arc-template.json and a block here (and a page in AppPages, a catalog in AppSetup).
        services.AddKnowledge();
        services.AddAgents();
        services.AddFlow();
        services.AddPrompts();
        services.AddTeam();
        services.AddUsage();
        if (ApiSettings.Current.UsesApi)
        {
            services.AddSingleton<ApiSession>();
            services.AddSingleton<TokenStore>();
            services.AddSingleton<ApiClient>();
            services.AddSingleton<SignInViewModel>();
            services.Replace(ServiceDescriptor.Singleton<IModelsService, ApiModelsService>());
            services.Replace(ServiceDescriptor.Singleton<IChatService, ApiChatService>());
            services.Replace(ServiceDescriptor.Singleton<IKnowledgeService, ApiKnowledgeService>());
            services.Replace(ServiceDescriptor.Singleton<IAgentsService, ApiAgentsService>());
            services.Replace(ServiceDescriptor.Singleton<IFlowService, ApiFlowService>());
            services.Replace(ServiceDescriptor.Singleton<IPromptsService, ApiPromptsService>());
            services.Replace(ServiceDescriptor.Singleton<ITeamService, ApiTeamService>());
            services.Replace(ServiceDescriptor.Singleton<IUsageService, ApiUsageService>());
        }

        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
