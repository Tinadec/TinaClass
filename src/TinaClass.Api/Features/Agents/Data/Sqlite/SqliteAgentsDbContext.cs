using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Agents.Data.Sqlite;

/// <summary>The Sqlite flavor of <see cref="AgentsDbContext"/>, with its own migrations (<c>Migrations/</c> next to this file).</summary>
public sealed class SqliteAgentsDbContext(DbContextOptions<SqliteAgentsDbContext> options) : AgentsDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => FeatureDb.ApplySqliteConventions(configurationBuilder);
}

internal sealed class SqliteAgentsDbContextFactory : IDesignTimeDbContextFactory<SqliteAgentsDbContext>
{
    public SqliteAgentsDbContext CreateDbContext(string[] args) => new(FeatureDb.DesignTimeSqlite<SqliteAgentsDbContext>(AgentsDbContext.Name));
}
