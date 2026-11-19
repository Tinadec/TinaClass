using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Auth.Data;
using TinaClass.Contracts.Sample;

namespace TinaClass.Api.Features.Auth;

/// <summary>
/// <c>Bootstrap:AdminEmail</c> / <c>AdminPassword</c> / <c>AdminName</c>: the first administrator, created at start when there are
/// no accounts yet (production's first run). Remove them from the configuration afterwards.
/// </summary>
public sealed class BootstrapOptions
{
    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }

    public string? AdminName { get; set; }
}

/// <summary><c>Jobs:SessionCleanupMinutes</c>: how often expired and revoked sessions are deleted; 0 turns the job off.</summary>
public sealed class JobsOptions
{
    public int SessionCleanupMinutes { get; set; } = 60;
}

internal sealed partial class AuthInitializer(IOptions<BootstrapOptions> bootstrap, JwtKeyProvider keys, ILogger<AuthInitializer> logger) : IDatabaseInitializer
{
    public int Order => 10;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // Resolving the key here makes a missing Jwt:SigningKey stop the start, not the first sign-in.
        if (keys.Generated)
        {
            LogGeneratedKey(logger);
        }
        var o = bootstrap.Value;
        if (string.IsNullOrEmpty(o.AdminEmail) || string.IsNullOrEmpty(o.AdminPassword))
        {
            return;
        }
        var accounts = services.GetRequiredService<UserAccounts>();
        if (await accounts.CountAsync(cancellationToken) > 0)
        {
            return;
        }
        await accounts.CreateAsync(o.AdminEmail, o.AdminName ?? o.AdminEmail.Split('@')[0], o.AdminPassword, Roles.Owner, cancellationToken);
        LogAdminCreated(logger, o.AdminEmail);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jwt:SigningKey is not set: a key was made up for this run (Development). Tokens stop working when the API restarts.")]
    private static partial void LogGeneratedKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the first administrator, {Email}. Remove Bootstrap:* from the configuration.")]
    private static partial void LogAdminCreated(ILogger logger, string email);
}

/// <summary>The sample users of a Development database (栖云科技): the owner, an administrator and three members, with the same password.</summary>
internal sealed class AuthSeeder : ISampleSeeder
{
    public int Order => 10;

    public async Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken)
    {
        var accounts = services.GetRequiredService<UserAccounts>();
        if (await accounts.CountAsync(cancellationToken) > 0)
        {
            return;
        }
        foreach (var person in SampleCore.People)
        {
            await accounts.CreateAsync(person.Email, person.Name, sample.Password, person.Role, cancellationToken, person.Id);
        }
    }
}

/// <summary>Deletes sessions that expired or were revoked more than a day ago.</summary>
public sealed class SessionCleaner(AuthDbContext db, TimeProvider time)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().AddDays(-1);
        return await db.Sessions.Where(s => s.ExpiresAt < cutoff || (s.RevokedAt != null && s.RevokedAt < cutoff)).ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>Runs <see cref="SessionCleaner"/> every <c>Jobs:SessionCleanupMinutes</c> minutes (tests turn it off with 0 and call it directly).</summary>
internal sealed partial class SessionCleanupJob(IServiceScopeFactory scopes, IOptions<JobsOptions> options, TimeProvider time, ILogger<SessionCleanupJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = options.Value.SessionCleanupMinutes;
        if (minutes <= 0)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var removed = await scope.ServiceProvider.GetRequiredService<SessionCleaner>().RunAsync(stoppingToken);
                LogRemoved(logger, removed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed {Count} old sessions")]
    private static partial void LogRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The session cleanup failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
