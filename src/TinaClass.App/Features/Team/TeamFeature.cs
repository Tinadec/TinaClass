using TinaClass.App.Common.Api;
using TinaClass.App.Features.Team.Localization;
using TinaClass.Contracts.Common;
using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Features.Team;

/// <summary>Team (<c>features.team</c>; needs the API): members and invitations, and the audit log.</summary>
public static class TeamFeature
{
    public static IServiceCollection AddTeam(this IServiceCollection services)
    {
        services.AddSingleton<ITeamService, SampleTeamService>();
        services.AddSingleton<TeamViewModel>();
        // The text of the error the server answers with (409) when this feature's rule is broken.
        ErrorText.Register(ErrorCodes.LastOwner, () => TeamLangResources.Get(TeamLangResourceKind.ErrorLastOwner));
        return services;
    }
}
