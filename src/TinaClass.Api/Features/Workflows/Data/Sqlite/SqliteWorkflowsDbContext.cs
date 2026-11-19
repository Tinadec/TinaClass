using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Workflows.Data.Sqlite;

/// <summary>The Sqlite flavor of <see cref="WorkflowsDbContext"/>, with its own migrations (<c>Migrations/</c> next to this file).</summary>
public sealed class SqliteWorkflowsDbContext(DbContextOptions<SqliteWorkflowsDbContext> options) : WorkflowsDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => FeatureDb.ApplySqliteConventions(configurationBuilder);
}

internal sealed class SqliteWorkflowsDbContextFactory : IDesignTimeDbContextFactory<SqliteWorkflowsDbContext>
{
    public SqliteWorkflowsDbContext CreateDbContext(string[] args) => new(FeatureDb.DesignTimeSqlite<SqliteWorkflowsDbContext>(WorkflowsDbContext.Name));
}
