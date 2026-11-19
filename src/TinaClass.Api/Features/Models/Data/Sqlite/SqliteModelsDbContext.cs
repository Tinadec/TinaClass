using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Models.Data.Sqlite;

/// <summary>The Sqlite flavor of <see cref="ModelsDbContext"/>, with its own migrations (<c>Migrations/</c> next to this file).</summary>
public sealed class SqliteModelsDbContext(DbContextOptions<SqliteModelsDbContext> options) : ModelsDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => FeatureDb.ApplySqliteConventions(configurationBuilder);
}

internal sealed class SqliteModelsDbContextFactory : IDesignTimeDbContextFactory<SqliteModelsDbContext>
{
    public SqliteModelsDbContext CreateDbContext(string[] args) => new(FeatureDb.DesignTimeSqlite<SqliteModelsDbContext>(ModelsDbContext.Name));
}
