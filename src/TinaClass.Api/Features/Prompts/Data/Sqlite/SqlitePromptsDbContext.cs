using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Prompts.Data.Sqlite;

/// <summary>The Sqlite flavor of <see cref="PromptsDbContext"/>, with its own migrations (<c>Migrations/</c> next to this file).</summary>
public sealed class SqlitePromptsDbContext(DbContextOptions<SqlitePromptsDbContext> options) : PromptsDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => FeatureDb.ApplySqliteConventions(configurationBuilder);
}

internal sealed class SqlitePromptsDbContextFactory : IDesignTimeDbContextFactory<SqlitePromptsDbContext>
{
    public SqlitePromptsDbContext CreateDbContext(string[] args) => new(FeatureDb.DesignTimeSqlite<SqlitePromptsDbContext>(PromptsDbContext.Name));
}
