using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Knowledge.Data.Sqlite;

/// <summary>The Sqlite flavor of <see cref="KnowledgeDbContext"/>, with its own migrations (<c>Migrations/</c> next to this file).</summary>
public sealed class SqliteKnowledgeDbContext(DbContextOptions<SqliteKnowledgeDbContext> options) : KnowledgeDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => FeatureDb.ApplySqliteConventions(configurationBuilder);
}

internal sealed class SqliteKnowledgeDbContextFactory : IDesignTimeDbContextFactory<SqliteKnowledgeDbContext>
{
    public SqliteKnowledgeDbContext CreateDbContext(string[] args) => new(FeatureDb.DesignTimeSqlite<SqliteKnowledgeDbContext>(KnowledgeDbContext.Name));
}
