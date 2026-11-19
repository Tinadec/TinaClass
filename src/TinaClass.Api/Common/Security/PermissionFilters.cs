using TinaClass.Api.Common.Http;

namespace TinaClass.Api.Common.Security;

/// <summary>Endpoint conventions: who may call an endpoint. Every endpoint of a feature uses one of these.</summary>
public static class PermissionFilters
{
    /// <summary>A signed-in actor with <paramref name="permission"/>; 401 otherwise, 403 without the permission.</summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission) =>
        builder.RequireAuthorization().AddEndpointFilter(async (context, next) =>
        {
            var actors = context.HttpContext.RequestServices.GetRequiredService<IActorContext>();
            var actor = await actors.RequireAsync(context.HttpContext.RequestAborted);
            if (!actor.Can(permission))
            {
                throw ApiException.Forbidden();
            }
            return await next(context);
        });

    /// <summary>Any signed-in actor (reading one's own account).</summary>
    public static RouteHandlerBuilder RequireActor(this RouteHandlerBuilder builder) =>
        builder.RequireAuthorization().AddEndpointFilter(async (context, next) =>
        {
            var actors = context.HttpContext.RequestServices.GetRequiredService<IActorContext>();
            await actors.RequireAsync(context.HttpContext.RequestAborted);
            return await next(context);
        });
}
