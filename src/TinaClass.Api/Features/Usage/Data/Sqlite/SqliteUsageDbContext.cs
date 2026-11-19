using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Usage.Data.Sqlite;

/// <summary>The Sqlite flavor of <see cref="UsageDbContext"/>, with its own migrations (<c>Migrations/</c> next to this file).</summary>
public sealed class SqliteUsageDbContext(DbContextOptions<SqliteUsageDbContext> options) : UsageDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => FeatureDb.ApplySqliteConventions(configurationBuilder);
}

internal sealed class SqliteUsageDbContextFactory : IDesignTimeDbContextFactory<SqliteUsageDbContext>
{
    public SqliteUsageDbContext CreateDbContext(string[] args) => new(FeatureDb.DesignTimeSqlite<SqliteUsageDbContext>(UsageDbContext.Name));
}
