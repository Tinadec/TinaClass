using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Auth.Data;
using TinaClass.Contracts.Auth;

namespace TinaClass.Api.Features.Auth;

/// <summary>The signed-in user's own account: GET /me.</summary>
public sealed class AccountService(AuthDbContext db, IActorContext actors)
{
    public async Task<CurrentUser> GetAsync(CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var email = await db.Users.AsNoTracking().Where(u => u.Id == actor.Id).Select(u => u.Email).SingleAsync(cancellationToken);
        return new CurrentUser(actor.Id!.Value, email, actor.Name, actor.RoleId ?? "", [.. actor.Permissions.Order(StringComparer.Ordinal)]);
    }
}
