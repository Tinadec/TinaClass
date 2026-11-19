using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.Api.Common;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Models.Data;
using TinaClass.Api.Features.Models.Data.Sqlite;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Models;
using TinaClass.Contracts.Sample;

namespace TinaClass.Api.Features.Models;

/// <summary>/providers and /models: model providers, their keys and the models they serve; the adapters chat and agents talk to (the core feature).</summary>
public static class ModelsFeature
{
    public static IServiceCollection AddModelsFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFeatureDb<ModelsDbContext>(ModelsDbContext.Name)
                .WithSqlite<SqliteModelsDbContext>()
                ;
        services.AddOptions<EchoOptions>().Bind(configuration.GetSection("Models:Echo"));
        services.AddHttpClient(OpenAiCompatibleProvider.ClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<KeyVault>();
        services.AddSingleton<IModelProvider, EchoProvider>();
        services.AddSingleton<IModelProvider>(sp => new OpenAiCompatibleProvider(sp.GetRequiredService<IHttpClientFactory>(), ProviderProtocols.OpenAiCompatible));
        services.AddSingleton<IModelProvider>(sp => new OpenAiCompatibleProvider(sp.GetRequiredService<IHttpClientFactory>(), ProviderProtocols.Local));
        services.AddScoped<ProviderProbe>();
        services.AddScoped<ModelService>();
        services.Replace(ServiceDescriptor.Scoped<IModelCatalog, ModelCatalog>());
        services.AddScoped<IModelGateway, ModelGateway>();
        services.AddSingleton<IDatabaseInitializer, ModelsInitializer>();
        services.AddSingleton<ISampleSeeder, ModelsSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapModelsFeature(this IEndpointRouteBuilder endpoints)
    {
        var providers = endpoints.MapGroup(ApiRoutes.Providers).WithTags("Models");
        providers.MapGet("", (ModelService s, CancellationToken ct) => s.ListProvidersAsync(ct)).RequirePermission(Permissions.ModelsManage);
        providers.MapPost("", async (CreateProviderRequest r, ModelService s, CancellationToken ct) =>
        {
            var p = await s.CreateProviderAsync(r, ct);
            return Results.Created($"{ApiRoutes.Providers}/{p.Id}", p);
        }).RequirePermission(Permissions.ModelsManage);
        providers.MapPatch("/{id:guid}", (Guid id, UpdateProviderRequest r, ModelService s, CancellationToken ct) => s.UpdateProviderAsync(id, r, ct)).RequirePermission(Permissions.ModelsManage);
        providers.MapDelete("/{id:guid}", async (Guid id, ModelService s, CancellationToken ct) =>
        {
            await s.DeleteProviderAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ModelsManage);
        providers.MapPost("/{id:guid}/test", (Guid id, ModelService s, CancellationToken ct) => s.TestAsync(id, ct)).RequirePermission(Permissions.ModelsManage);
        providers.MapPost("/{id:guid}/sync", (Guid id, ModelService s, CancellationToken ct) => s.SyncAsync(id, ct)).RequirePermission(Permissions.ModelsManage);
        // The key goes in and never comes out: these answer with the provider (its last four characters only).
        providers.MapPut("/{id:guid}/key", (Guid id, SetKeyRequest r, ModelService s, CancellationToken ct) => s.SetKeyAsync(id, r, ct)).RequirePermission(Permissions.KeysManage);
        providers.MapDelete("/{id:guid}/key", (Guid id, ModelService s, CancellationToken ct) => s.ClearKeyAsync(id, ct)).RequirePermission(Permissions.KeysManage);

        var models = endpoints.MapGroup(ApiRoutes.Models).WithTags("Models");
        models.MapGet("", async (bool? all, IActorContext actors, ModelService s, CancellationToken ct) =>
        {
            var actor = await actors.RequireAsync(ct);
            return await s.ListModelsAsync(all == true && actor.Can(Permissions.ModelsManage), ct);
        }).RequirePermission(Permissions.ModelsUse);
        models.MapPatch("/{id:guid}", (Guid id, UpdateModelRequest r, ModelService s, CancellationToken ct) => s.UpdateModelAsync(id, r, ct)).RequirePermission(Permissions.ModelsManage);
        return endpoints;
    }
}

/// <summary>
/// Makes sure the built-in Echo provider and its model exist, and that some chat model is the default, so that the chat works from the first start.
/// The template options <c>defaultProvider</c> and <c>defaultModelCode</c> (<c>Models:DefaultProvider</c>, <c>Models:DefaultModelCode</c>) add one
/// provider of that kind with a model of that code and make it the default; its key is set at run time (the Models page, or
/// <c>Models__Keys__Default provider</c>), never here. Without a key the chat answers with a problem that says so, and Echo stays one click away.
/// </summary>
internal sealed class ModelsInitializer(IConfiguration configuration) : IDatabaseInitializer
{
    public const string DefaultProviderName = "Default provider";

    public int Order => 20;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ModelsDbContext>();
        var echo = DefaultModelCatalog.Echo;
        await EnsureDefaultProviderAsync(db, cancellationToken);
        // Saved before Echo looks for a default: its model is only in the change tracker until now, so Echo would also become the default.
        await db.SaveChangesAsync(cancellationToken);
        if (!await db.Providers.AnyAsync(p => p.Id == echo.ProviderId, cancellationToken))
        {
            db.Providers.Add(new AiProvider { Id = echo.ProviderId, Name = "TinaClass Echo", Protocol = ProviderProtocols.Echo, BaseUrl = echo.BaseUrl, SortOrder = 99, Status = "ok" });
        }
        if (!await db.ModelEntries.AnyAsync(m => m.Id == echo.Id, cancellationToken))
        {
            db.ModelEntries.Add(new AiModel { Id = echo.Id, ProviderId = echo.ProviderId, Code = echo.Code, DisplayName = "Echo", ContextTokens = echo.ContextTokens, Caps = "demo", Efforts = "fast",
                Note = "No key needed: replays a sample answer.", IsDefault = !await db.ModelEntries.AnyAsync(m => m.IsDefault, cancellationToken) });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDefaultProviderAsync(ModelsDbContext db, CancellationToken cancellationToken)
    {
        var protocol = configuration["Models:DefaultProvider"] ?? ProviderProtocols.Echo;
        var code = configuration["Models:DefaultModelCode"]?.Trim();
        var baseUrl = protocol switch
        {
            ProviderProtocols.OpenAiCompatible => "https://api.example.com/v1",
            ProviderProtocols.Messages => "https://api.example.com",
            ProviderProtocols.Local => "http://127.0.0.1:11434",
            _ => null,
        };
        if (baseUrl is null || string.IsNullOrEmpty(code) || await db.Providers.AnyAsync(p => p.Name == DefaultProviderName, cancellationToken))
        {
            return;
        }
        var provider = new AiProvider { Id = Guid.NewGuid(), Name = DefaultProviderName, Protocol = protocol, BaseUrl = baseUrl, SortOrder = 0, Status = protocol == ProviderProtocols.Local ? "ok" : "no_key" };
        db.Providers.Add(provider);
        var current = await db.ModelEntries.Where(m => m.IsDefault).ToListAsync(cancellationToken);
        foreach (var m in current)
        {
            m.IsDefault = false;
        }
        db.ModelEntries.Add(new AiModel { Id = Guid.NewGuid(), ProviderId = provider.Id, Code = code, DisplayName = code, ContextTokens = 128_000, Caps = "tools", Efforts = "balanced", IsDefault = true,
            Note = "The default model of this installation (template options defaultProvider and defaultModelCode)." });
    }
}

/// <summary>The sample providers and models of 栖云科技 (design 16). Their hosts end in <c>.example</c>, so the gateway answers for them with Echo.</summary>
internal sealed class ModelsSeeder : ISampleSeeder
{
    public int Order => 20;

    public async Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ModelsDbContext>();
        var vault = services.GetRequiredService<KeyVault>();
        if (await db.Providers.AnyAsync(p => p.Id == SampleIds.Provider(1), cancellationToken))
        {
            return;
        }
        foreach (var p in SampleCore.Providers.Where(p => p.Protocol != ProviderProtocols.Echo))
        {
            db.Providers.Add(new AiProvider
            {
                Id = p.Id, Name = p.Name, Protocol = p.Protocol, BaseUrl = p.BaseUrl, SortOrder = p.Order, Status = p.Status,
                LastLatencyMs = p.LatencyMs, LastCheckAt = p.LatencyMs is null ? null : sample.Now.AddMinutes(-2),
                KeyCipher = p.KeyLast4 is null ? null : vault.Protect("sk-sample-" + p.KeyLast4), KeyHint = p.KeyLast4 is null ? null : "••••••••" + p.KeyLast4,
            });
        }
        // The sample's default is Aurora Pro: Echo gives the default up.
        await db.ModelEntries.Where(m => m.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDefault, false), cancellationToken);
        foreach (var m in SampleCore.Models.Where(m => m.ProviderId != SampleIds.Provider(5)))
        {
            db.ModelEntries.Add(new AiModel
            {
                Id = m.Id, ProviderId = m.ProviderId, Code = m.Code, DisplayName = m.Name, Kind = m.Kind, ContextTokens = m.Context, PriceIn = m.PriceIn, PriceOut = m.PriceOut, PriceCache = m.PriceCache,
                Caps = string.Join(',', m.Caps), Efforts = string.Join(',', m.Efforts), Note = m.Note, Enabled = m.Enabled, IsDefault = m.IsDefault,
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
