using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Team.Data.Sqlite;

/// <summary>The Sqlite flavor of <see cref="TeamDbContext"/>, with its own migrations (<c>Migrations/</c> next to this file).</summary>
public sealed class SqliteTeamDbContext(DbContextOptions<SqliteTeamDbContext> options) : TeamDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => FeatureDb.ApplySqliteConventions(configurationBuilder);
}

internal sealed class SqliteTeamDbContextFactory : IDesignTimeDbContextFactory<SqliteTeamDbContext>
{
    public SqliteTeamDbContext CreateDbContext(string[] args) => new(FeatureDb.DesignTimeSqlite<SqliteTeamDbContext>(TeamDbContext.Name));
}
