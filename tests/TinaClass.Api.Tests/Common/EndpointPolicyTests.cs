using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Common;

/// <summary>
/// Every endpoint of the API says who may call it. <c>RequirePermission</c> and <c>RequireActor</c> both call
/// <c>RequireAuthorization</c>; an endpoint with neither is open to anyone. Add a route to <see cref="Anonymous"/> only on purpose.
/// </summary>
public sealed class EndpointPolicyTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    /// <summary>The only anonymous routes of the API; health checks are outside <c>/api/v1</c>.</summary>
    private static readonly HashSet<string> Anonymous =
    [
        ApiRoutes.Auth + "/login",
        ApiRoutes.Auth + "/refresh",
        ApiRoutes.Auth + "/logout",
        ApiRoutes.Shared + "/{token}",
        ApiRoutes.Invites + "/accept",
    ];

    [Fact]
    public void Every_Api_Endpoint_Requires_Authorization_Except_The_Listed_Anonymous_Ones()
    {
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
                           .Where(e => e.RoutePattern.RawText is { } raw && raw.StartsWith(ApiRoutes.Base, StringComparison.Ordinal))
                           .ToList();
        Assert.NotEmpty(endpoints);

        var open = endpoints.Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null)
                            .Select(e => e.RoutePattern.RawText!)
                            .Where(route => !Anonymous.Contains(route.TrimEnd('/')))
                            .ToList();
        Assert.True(open.Count == 0, "These endpoints declare no access rule (use RequirePermission or RequireActor): " + string.Join(", ", open));
    }
}
