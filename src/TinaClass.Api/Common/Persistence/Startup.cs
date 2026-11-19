using Microsoft.Extensions.Options;

namespace TinaClass.Api.Common.Persistence;

/// <summary>Work that runs once at start after the migrations, in <see cref="Order"/> (the first administrator).</summary>
public interface IDatabaseInitializer
{
    int Order { get; }

    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>
/// Fills a feature's empty tables with sample data (Development only, <c>Seed:SampleData</c>). Seeders run in
/// <see cref="Order"/>: auth (the users) 10, models 20, knowledge 30, prompts 35, chat 40, agents 50, workflows 55, usage 60, team 70.
/// </summary>
public interface ISampleSeeder
{
    int Order { get; }

    Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken);
}

public sealed class SeedOptions
{
    /// <summary>Fill an empty database with sample data. Development only: elsewhere the API refuses to start with it on.</summary>
    public bool SampleData { get; set; }

    /// <summary>The password of every sample user. Local development only.</summary>
    public string? Password { get; set; }
}

/// <summary>The moment the sample data is relative to, so that it always looks recent.</summary>
public sealed record SampleContext(DateTimeOffset Now, string Password)
{
    /// <summary>Today at a time of day (UTC), or that many days earlier.</summary>
    public DateTimeOffset At(int daysAgo, int hour, int minute) =>
        new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-daysAgo).AddHours(hour).AddMinutes(minute);
}

public static class DatabaseStartup
{
    /// <summary>
    /// Migrates every feature's database (unless <c>Database:MigrateOnStart</c> is off), runs the initializers and, in Development
    /// with <c>Seed:SampleData</c>, seeds the sample data. Sample data in any other environment is an error: its password is
    /// public.
    /// </summary>
    public static async Task PrepareDatabaseAsync(this IServiceProvider root, CancellationToken cancellationToken = default)
    {
        await using var scope = root.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = services.GetRequiredService<IOptions<SeedOptions>>().Value;
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (seed.SampleData && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Seed:SampleData is for the Development environment only: its users share a public password. Turn it off.");
        }
        // Creating the initializers first makes a missing secret (Jwt:SigningKey) stop the start before any database is touched.
        var initializers = services.GetServices<IDatabaseInitializer>().OrderBy(i => i.Order).ToList();
        if (services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStart)
        {
            foreach (var database in services.GetServices<IFeatureDatabase>())
            {
                await database.MigrateAsync(services, cancellationToken);
            }
        }
        foreach (var initializer in initializers)
        {
            await initializer.InitializeAsync(services, cancellationToken);
        }
        if (seed.SampleData)
        {
            var password = string.IsNullOrEmpty(seed.Password)
                ? throw new InvalidOperationException("Seed:SampleData needs Seed:Password (the sample users' password).")
                : seed.Password;
            var sample = new SampleContext(services.GetRequiredService<TimeProvider>().GetUtcNow(), password);
            foreach (var seeder in services.GetServices<ISampleSeeder>().OrderBy(s => s.Order))
            {
                await seeder.SeedAsync(services, sample, cancellationToken);
            }
        }
    }
}
