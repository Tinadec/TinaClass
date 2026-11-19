using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Email;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Common.Security;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Common;

/// <summary><c>App:Name</c>: the product name in emails (the template's appName parameter).</summary>
public sealed class AppOptions
{
    public string Name { get; set; } = "TinaClass";
}

/// <summary>What every feature relies on: JSON, errors, authentication plumbing, persistence, email and the seams.</summary>
public static class CommonServices
{
    public const string AuthRateLimit = "auth";

    public static WebApplicationBuilder AddCommon(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddOptions<AppOptions>().Bind(configuration.GetSection("App"));
        // The currency of the amounts in answers and logs (template option currency): CNY or USD.
        TinaClass.Contracts.Common.Money.Configure(configuration["App:Currency"]);
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection("Seed"));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection("Email"));
        services.AddOptions<DatabaseOptions>().Configure<IConfiguration>((options, config) =>
        {
            config.GetSection("Database").Bind(options);
            options.SqliteConnectionString ??= config.GetConnectionString("Sqlite");
            options.PostgresConnectionString ??= config.GetConnectionString("Postgres");
            options.SqlServerConnectionString ??= config.GetConnectionString("SqlServer");
        });

        // Every error is problem details with a "code": the ones a handler throws (ApiException) and the bare 401, 403, 404 and 429 of
        // the framework (the status code pages fill them in, and this adds the code).
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            if (!context.ProblemDetails.Extensions.ContainsKey("code") && CodeFor(context.ProblemDetails.Status) is { } code)
            {
                context.ProblemDetails.Extensions["code"] = code;
            }
        });
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ContractsJsonContext.Default);
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);

        // Keys that encrypt secrets which must be read back (a TOTP secret, a webhook signing secret) once a feature adds them. In
        // a container the folder is a volume: without it the keys are lost when the container is replaced.
        var keys = configuration["DataProtection:KeysPath"];
        var protection = services.AddDataProtection().SetApplicationName("tinaclass-api");
        if (!string.IsNullOrEmpty(keys))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(keys));
        }

        services.AddAuthentication(AuthSchemes.Default)
                .AddPolicyScheme(AuthSchemes.Default, "Token or API key", options => options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer " + AuthSchemes.ApiKeyPrefix, StringComparison.Ordinal)
                        ? AuthSchemes.ApiKey
                        : AuthSchemes.Jwt);
        services.AddAuthorization();

        var signInLimit = configuration.GetValue("Auth:SignInRequestsPerMinute", 20);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = signInLimit, Window = TimeSpan.FromMinutes(1) }));
        });

        // Off by default: trusting X-Forwarded-For from anyone lets a client choose its own address (and so dodge the rate limit).
        // Turn it on only when the API is reachable through your reverse proxy alone.
        if (configuration.GetValue("ForwardedHeaders:Enabled", false))
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        if (string.Equals(configuration["Email:Mode"], "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.TryAddSingleton<IEmailSender, LogEmailSender>();
        }

        // Seams the optional features and your own code replace (Replace, not TryAdd, in their Add* methods).
        services.AddScoped<IAuditLog, NullAuditLog>();
        services.AddScoped<IEventPublisher, NullEventPublisher>();
        services.AddScoped<IModelCatalog, DefaultModelCatalog>();
        services.AddScoped<IKnowledgeRetriever, NullKnowledgeRetriever>();
        services.AddScoped<IToolCatalog, NullToolCatalog>();
        services.AddScoped<IPromptResolver, NullPromptResolver>();
        services.AddScoped<IUsageRecorder, NullUsageRecorder>();
        services.AddScoped<IBudgetGuard, NullBudgetGuard>();
        services.AddScoped<IApprovalSink, RejectingApprovalSink>();
        return builder;
    }

    public static WebApplication UseCommon(this WebApplication app)
    {
        if (app.Configuration.GetValue("ForwardedHeaders:Enabled", false))
        {
            app.UseForwardedHeaders();
        }
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        // /healthz: the process answers. /readyz: every feature's database does too.
        app.MapGet("/healthz", () => Results.Ok()).ExcludeFromDescription();
        app.MapGet("/readyz", async (IServiceProvider services, CancellationToken ct) =>
        {
            foreach (var database in services.GetServices<IFeatureDatabase>())
            {
                if (!await database.CanConnectAsync(services, ct))
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
            }
            return Results.Ok();
        }).ExcludeFromDescription();
        return app;
    }

    private static string? CodeFor(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.Validation,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        StatusCodes.Status429TooManyRequests => ErrorCodes.RateLimited,
        _ => null,
    };
}
