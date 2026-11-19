using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.Api.Common;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Team.Data;
using TinaClass.Api.Features.Team.Data.Sqlite;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Sample;
using TinaClass.Contracts.Team;

namespace TinaClass.Api.Features.Team;

/// <summary>/members, /invites and /audit (design 4.8; optional, needs the backend). Also stores every feature's audit records (it replaces the null <see cref="IAuditLog"/>).</summary>
public static class TeamFeature
{
    public static IServiceCollection AddTeamFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFeatureDb<TeamDbContext>(TeamDbContext.Name)
                .WithSqlite<SqliteTeamDbContext>()
                ;
        services.AddOptions<TeamOptions>().Bind(configuration.GetSection("Team"));
        services.AddScoped<TeamService>();
        services.Replace(ServiceDescriptor.Scoped<IAuditLog, TeamAuditLog>());
        services.AddSingleton<ISampleSeeder, TeamSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapTeamFeature(this IEndpointRouteBuilder endpoints)
    {
        var members = endpoints.MapGroup(ApiRoutes.Members).WithTags("Team");
        members.MapGet("", (TeamService s, CancellationToken ct) => s.MembersAsync(ct)).RequirePermission(Permissions.TeamMembersManage);
        members.MapPatch("/{id:guid}", (Guid id, UpdateMemberRequest r, TeamService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(Permissions.TeamMembersManage);
        members.MapDelete("/{id:guid}", async (Guid id, TeamService s, CancellationToken ct) =>
        {
            await s.RemoveAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.TeamMembersManage);

        var invites = endpoints.MapGroup(ApiRoutes.Invites).WithTags("Team");
        invites.MapPost("", async (InviteRequest r, TeamService s, CancellationToken ct) => Results.Created(ApiRoutes.Invites, await s.InviteAsync(r, ct))).RequirePermission(Permissions.TeamMembersManage);
        // The invitee has no account yet: the token is the credential (anonymous, rate limited like sign-in).
        invites.MapPost("/accept", (AcceptInviteRequest r, TeamService s, CancellationToken ct) => s.AcceptAsync(r, ct)).RequireRateLimiting(CommonServices.AuthRateLimit);

        endpoints.MapGroup(ApiRoutes.Audit).WithTags("Team")
                 .MapGet("", (string? action, int? page, int? pageSize, TeamService s, CancellationToken ct) => s.AuditAsync(action, page, pageSize, ct)).RequirePermission(Permissions.TeamAuditRead);
        return endpoints;
    }
}

/// <summary>The sample audit trail (design 16) and the pending invitation of 徐明哲; their token is not known to anyone.</summary>
internal sealed class TeamSeeder : ISampleSeeder
{
    public int Order => 70;

    public async Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<TeamDbContext>();
        if (await db.Audit.AnyAsync(cancellationToken))
        {
            return;
        }
        var directory = services.GetRequiredService<IUserDirectory>();
        foreach (var (actorId, action, kind, detail, daysAgo, hour) in TeamSample.Audit)
        {
            var actor = await directory.FindAsync(actorId, cancellationToken);
            db.Audit.Add(new AuditEntry { Id = Guid.NewGuid(), ActorId = actorId, ActorName = actor?.Name ?? "", Action = action, TargetKind = kind, TargetId = "", Detail = detail, At = sample.At(daysAgo, hour, 0) });
        }
        db.Invites.Add(new Invite
        {
            Id = Guid.NewGuid(),
            Email = TeamSample.InvitedEmail,
            NormalizedEmail = TeamSample.InvitedEmail,
            RoleId = Roles.Member,
            TokenHash = Secrets.Hash(Secrets.Token()),
            InvitedBy = SampleIds.Admin,
            CreatedAt = sample.At(2, 16, 0),
            ExpiresAt = sample.Now.AddDays(5),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
