using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Features.Agents;

/// <summary>Agents (<c>features.agents</c>): the agents page, the run card, the approval dialog and the sidebar's count of approvals that wait.</summary>
public static class AgentsFeature
{
    public static IServiceCollection AddAgents(this IServiceCollection services)
    {
        services.AddSingleton<IAgentsService, SampleAgentsService>();
        services.AddSingleton<AgentsViewModel>();
        services.AddSingleton<IBadgeSource, AgentsBadge>();
        services.AddSingleton<IApprovalReview, ApprovalReview>();
        return services;
    }
}
