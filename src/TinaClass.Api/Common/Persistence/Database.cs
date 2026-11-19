using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;

namespace TinaClass.Api.Common.Persistence;

public enum DatabaseProvider
{
    Sqlite,
    Postgres,
    SqlServer,
}

/// <summary>
/// <c>Database:Provider</c> (<c>Sqlite</c>, <c>Postgres</c> or <c>SqlServer</c>) and its connection string
/// (<c>ConnectionStrings:Sqlite</c>, <c>:Postgres</c> or <c>:SqlServer</c>). Connection strings carry passwords: they come from the
/// environment or a secret store, never from a file in the repository.
/// </summary>
public sealed class DatabaseOptions
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Apply the migrations of every feature when the API starts. Turn it off to run them from a pipeline instead.</summary>
    public bool MigrateOnStart { get; set; } = true;

    public string? SqliteConnectionString { get; set; }

    public string? PostgresConnectionString { get; set; }

    public string? SqlServerConnectionString { get; set; }

    public string ResolveConnectionString() => Provider switch
    {
        DatabaseProvider.Sqlite => SqliteConnectionString ?? "Data Source=tinaclass.db",
        DatabaseProvider.Postgres => PostgresConnectionString
            ?? throw new InvalidOperationException("Set ConnectionStrings:Postgres (or the environment variable ConnectionStrings__Postgres)."),
        _ => SqlServerConnectionString
            ?? throw new InvalidOperationException("Set ConnectionStrings:SqlServer (or the environment variable ConnectionStrings__SqlServer)."),
    };
}

/// <summary>
/// Registers a feature's DbContext: the base type the feature uses, and one subclass per database provider, each with its own
/// migrations (<c>Features/&lt;Feature&gt;/Data/Sqlite</c>, <c>…/Postgres</c>, <c>…/SqlServer</c>) and migrations history table.
/// </summary>
public sealed class FeatureDbBuilder<TContext>(IServiceCollection services, string name) where TContext : DbContext
{
    internal Dictionary<DatabaseProvider, Type> Implementations { get; } = [];

    public FeatureDbBuilder<TContext> WithSqlite<TImplementation>() where TImplementation : TContext
    {
        services.AddDbContext<TImplementation>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ResolveConnectionString(),
                sqlite => sqlite.MigrationsHistoryTable(FeatureDb.HistoryTable(name))));
        Implementations[DatabaseProvider.Sqlite] = typeof(TImplementation);
        return this;
    }


}

public static class FeatureDb
{
    public static string HistoryTable(string name) => $"__{name}_migrations";

    /// <summary>Registers <typeparamref name="TContext"/>; chain <c>WithSqlite</c> / <c>WithPostgres</c> / <c>WithSqlServer</c> with its subclasses.</summary>
    public static FeatureDbBuilder<TContext> AddFeatureDb<TContext>(this IServiceCollection services, string name) where TContext : DbContext
    {
        var builder = new FeatureDbBuilder<TContext>(services, name);
        services.AddScoped(sp =>
        {
            var provider = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider;
            return builder.Implementations.TryGetValue(provider, out var type)
                ? (TContext)sp.GetRequiredService(type)
                : throw new InvalidOperationException($"{typeof(TContext).Name} has no {provider} support in this build.");
        });
        services.AddSingleton<IFeatureDatabase>(new FeatureDatabase<TContext>(name));
        return builder;
    }

    /// <summary>Options for <c>dotnet ef</c> (design-time factories); no database is opened.</summary>
    public static DbContextOptions<T> DesignTimeSqlite<T>(string name) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlite("Data Source=design-time.db", s => s.MigrationsHistoryTable(HistoryTable(name))).Options;



    /// <summary>
    /// SQLite has no date-time-offset type that it can sort: store instants as ticks. Every SQLite subclass calls this from
    /// <c>ConfigureConventions</c>. (PostgreSQL stores <c>timestamptz</c> and SQL Server <c>datetimeoffset</c>; both need no
    /// conversion as long as every instant is written in UTC, which <c>TimeProvider.GetUtcNow()</c> gives.)
    /// </summary>
    public static void ApplySqliteConventions(ModelConfigurationBuilder configuration) =>
        configuration.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
}

/// <summary>A feature's database, migrated at start.</summary>
public interface IFeatureDatabase
{
    string Name { get; }

    Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken);

    Task<bool> CanConnectAsync(IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class FeatureDatabase<TContext>(string name) : IFeatureDatabase where TContext : DbContext
{
    public string Name => name;

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<TContext>().Database.MigrateAsync(cancellationToken);

    public Task<bool> CanConnectAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<TContext>().Database.CanConnectAsync(cancellationToken);
}
