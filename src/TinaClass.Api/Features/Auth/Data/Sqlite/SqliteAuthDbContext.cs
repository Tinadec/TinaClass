using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Auth.Data.Sqlite;

/// <summary>The Sqlite flavor of <see cref="AuthDbContext"/>, with its own migrations (<c>Migrations/</c> next to this file).</summary>
public sealed class SqliteAuthDbContext(DbContextOptions<SqliteAuthDbContext> options) : AuthDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => FeatureDb.ApplySqliteConventions(configurationBuilder);
}

internal sealed class SqliteAuthDbContextFactory : IDesignTimeDbContextFactory<SqliteAuthDbContext>
{
    public SqliteAuthDbContext CreateDbContext(string[] args) => new(FeatureDb.DesignTimeSqlite<SqliteAuthDbContext>(AuthDbContext.Name));
}
