using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Features.Models.Data;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Models;

namespace TinaClass.Api.Tests.Models;

/// <summary>The template options <c>defaultProvider</c> and <c>defaultModelCode</c> (<c>Models:DefaultProvider</c>, <c>Models:DefaultModelCode</c>).</summary>
public sealed class ModelsDefaultsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_default_provider_and_model_are_created_at_the_first_start_and_the_model_is_the_default()
    {
        // A host built from the shared factory would write into the shared database: give it one of its own.
        var connectionString = $"Data Source=file:tinaclass-defaults-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync(Ct);
        await using var configured = api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sqlite", connectionString);
            builder.UseSetting("Models:DefaultProvider", ProviderProtocols.OpenAiCompatible);
            builder.UseSetting("Models:DefaultModelCode", "gpt-example");
        });
        using var scope = configured.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModelsDbContext>();

        var provider = await db.Providers.AsNoTracking().SingleAsync(p => p.Name == "Default provider", Ct);
        Assert.Equal((ProviderProtocols.OpenAiCompatible, "no_key", null), (provider.Protocol, provider.Status, provider.KeyCipher));
        var defaults = await db.ModelEntries.AsNoTracking().Where(m => m.IsDefault).ToListAsync(Ct);
        var model = Assert.Single(defaults);
        Assert.Equal(("gpt-example", provider.Id), (model.Code, model.ProviderId));
    }

    [Fact]
    public async Task Without_the_options_Echo_stays_the_default()
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModelsDbContext>();

        Assert.False(await db.Providers.AnyAsync(p => p.Name == "Default provider", Ct));
        Assert.Equal("Echo", (await db.ModelEntries.AsNoTracking().SingleAsync(m => m.IsDefault, Ct)).DisplayName);
    }
}
