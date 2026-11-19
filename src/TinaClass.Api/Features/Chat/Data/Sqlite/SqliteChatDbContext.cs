using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Chat.Data.Sqlite;

/// <summary>The Sqlite flavor of <see cref="ChatDbContext"/>, with its own migrations (<c>Migrations/</c> next to this file).</summary>
public sealed class SqliteChatDbContext(DbContextOptions<SqliteChatDbContext> options) : ChatDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => FeatureDb.ApplySqliteConventions(configurationBuilder);
}

internal sealed class SqliteChatDbContextFactory : IDesignTimeDbContextFactory<SqliteChatDbContext>
{
    public SqliteChatDbContext CreateDbContext(string[] args) => new(FeatureDb.DesignTimeSqlite<SqliteChatDbContext>(ChatDbContext.Name));
}
