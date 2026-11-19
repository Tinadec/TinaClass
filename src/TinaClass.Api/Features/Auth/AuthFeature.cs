using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TinaClass.Api.Common;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Auth.Data;
using TinaClass.Api.Features.Auth.Data.Sqlite;
using TinaClass.Contracts.Auth;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Features.Auth;

/// <summary>Accounts and sign-in: /auth/* (anonymous, rate limited), /me and the user's API keys.</summary>
public static class AuthFeature
{
    public static WebApplicationBuilder AddAuthFeature(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddFeatureDb<AuthDbContext>(AuthDbContext.Name)
                .WithSqlite<SqliteAuthDbContext>()
                ;
        services.AddOptions<JwtOptions>().Bind(builder.Configuration.GetSection("Jwt"));
        services.AddOptions<BootstrapOptions>().Bind(builder.Configuration.GetSection("Bootstrap"));
        services.AddOptions<JobsOptions>().Bind(builder.Configuration.GetSection("Jobs"));
        services.AddSingleton<JwtKeyProvider>();
        services.AddSingleton<TokenIssuer>();
        services.AddSingleton<Passwords>();
        services.AddScoped<IActorContext, ActorContext>();
        services.AddScoped<AuthService>();
        services.AddScoped<AccountService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<UserAccounts>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<SessionCleaner>();
        services.AddHostedService<SessionCleanupJob>();
        services.AddSingleton<IDatabaseInitializer, AuthInitializer>();
        services.AddSingleton<ISampleSeeder, AuthSeeder>();

        services.AddAuthentication()
                .AddJwtBearer(AuthSchemes.Jwt, _ => { })
                .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>(AuthSchemes.ApiKey, null);
        services.AddOptions<JwtBearerOptions>(AuthSchemes.Jwt).Configure<JwtKeyProvider, IOptions<JwtOptions>, TimeProvider>((options, keys, jwt, time) =>
        {
            var skew = TimeSpan.FromSeconds(30);
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = jwt.Value.Issuer,
                ValidAudience = jwt.Value.Audience,
                IssuerSigningKey = keys.Key,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = AppClaims.Name,
                // The same clock as the rest of the API (TimeProvider), not the library's own.
                LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var now = time.GetUtcNow().UtcDateTime;
                    return (notBefore is null || notBefore.Value <= now + skew) && expires is not null && expires.Value > now - skew;
                },
            };
        });
        return builder;
    }

    public static IEndpointRouteBuilder MapAuthFeature(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup(ApiRoutes.Auth).WithTags("Auth").RequireRateLimiting(CommonServices.AuthRateLimit);
        auth.MapPost("/login", (LoginRequest request, AuthService service, CancellationToken ct) => service.LoginAsync(request, ct));
        auth.MapPost("/refresh", (RefreshRequest request, AuthService service, CancellationToken ct) => service.RefreshAsync(request, ct));
        auth.MapPost("/logout", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
        {
            await service.LogoutAsync(request, ct);
            return Results.NoContent();
        });

        var me = endpoints.MapGroup(ApiRoutes.Me).WithTags("Account");
        me.MapGet("", (AccountService account, CancellationToken ct) => account.GetAsync(ct)).RequireActor();

        var keys = endpoints.MapGroup(ApiRoutes.ApiKeys).WithTags("Account");
        keys.MapGet("", (ApiKeyService service, CancellationToken ct) => service.ListAsync(ct)).RequirePermission(Permissions.ApiKeysManage);
        keys.MapPost("", (CreateApiKeyRequest request, ApiKeyService service, CancellationToken ct) => service.CreateAsync(request, ct))
            .RequirePermission(Permissions.ApiKeysManage);
        keys.MapDelete("/{id:guid}", async (Guid id, ApiKeyService service, CancellationToken ct) =>
        {
            await service.RevokeAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ApiKeysManage);
        return endpoints;
    }
}
