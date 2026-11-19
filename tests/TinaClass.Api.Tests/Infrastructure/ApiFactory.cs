using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Email;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Features.Auth;
using TinaClass.Contracts;
using TinaClass.Contracts.Auth;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Infrastructure;

/// <summary>
/// The API in memory on a private in-memory SQLite database. Email, audit records and events are captured, time can be moved
/// forward, background jobs are off (tests run them directly) and the sign-in rate limit is out of the way. Users are created
/// directly (<see cref="CreateUserAsync"/>), so no sample data is needed.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "sample-password-for-tests";

    /// <summary>A signing key for the tests only: 32 or more characters, like the real one.</summary>
    public const string SigningKey = "test-only-signing-key-0123456789-abcdef";

    private readonly string _connectionString = $"Data Source=file:tinaclass-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private SqliteConnection? _keepAlive;

    public CapturingEmailSender Email { get; } = new();

    public RecordingAuditLog Audit { get; } = new();

    public RecordingEvents Events { get; } = new();

    public MovableTimeProvider Time { get; } = new();

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { TypeInfoResolver = ContractsJsonContext.Default };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Sqlite", _connectionString);
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jobs:SessionCleanupMinutes", "0");
        builder.UseSetting("Jobs:ChatPurgeMinutes", "0");
        builder.UseSetting("Knowledge:IndexerPollMs", "0");
        builder.UseSetting("Agents:StepDelayMs", "0");
        builder.UseSetting("Workflows:NodeDelayMs", "0");
        builder.UseSetting("Models:Echo:DelayMs", "0");
        builder.UseSetting("Models:Echo:ThinkMs", "0");
        builder.UseSetting("Auth:SignInRequestsPerMinute", "100000");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            // Audit records are captured and still passed on to the audit store, when the Team feature has one.
            var store = services.Last(d => d.ServiceType == typeof(IAuditLog));
            services.Remove(store);
            services.AddScoped<IAuditLog>(sp => new ForwardingAuditLog(Audit, (IAuditLog)ActivatorUtilities.CreateInstance(sp, store.ImplementationType!)));
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(Events);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    public async ValueTask InitializeAsync()
    {
        // An in-memory database lives while a connection to it is open.
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();
        _ = Server;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }
    }

    /// <summary>A client signed in as <paramref name="email"/>.</summary>
    public async Task<HttpClient> SignInAsync(string email, string password = Password) => Authorized((await LoginAsync(email, password)).AccessToken);

    public async Task<TokenPair> LoginAsync(string email, string password = Password)
    {
        using var anonymous = CreateClient();
        var response = await anonymous.PostAsJsonAsync(ApiRoutes.Auth + "/login", new LoginRequest(email, password, false), Json, TestContext.Current.CancellationToken);
        return await Expect.JsonAsync<TokenPair>(response);
    }

    /// <summary>A client that sends <paramref name="token"/> (an access token or an API key) as its Bearer credential.</summary>
    public HttpClient Authorized(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Client-Device", "Tests");
        return client;
    }

    /// <summary>Creates an account directly with the given role (<c>Roles.Admin</c>, <c>Member</c>, <c>Viewer</c>).</summary>
    public async Task<UserSummary> CreateUserAsync(string roleId, string? email = null, string name = "Test User")
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserAccounts>()
                          .CreateAsync(email ?? NewEmail(roleId), name, Password, roleId, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Runs every sample seeder (the sample workspace of design 16) as the Development start would: the five sample people, the providers, the
    /// conversations, bases, agents and so on. They sign in with <see cref="Password"/>. Safe to call twice.
    /// </summary>
    public async Task SeedSampleAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var sample = new SampleContext(Time.GetUtcNow(), Password);
        foreach (var seeder in scope.ServiceProvider.GetServices<ISampleSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(scope.ServiceProvider, sample, TestContext.Current.CancellationToken);
        }
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@example.com";
}

/// <summary>Keeps every email sent.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _messages = [];

    public IReadOnlyList<EmailMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Keeps every audit record.</summary>
public sealed class RecordingAuditLog : IAuditLog
{
    private readonly List<AuditRecord> _records = [];

    public IReadOnlyList<AuditRecord> Records
    {
        get
        {
            lock (_records)
            {
                return [.. _records];
            }
        }
    }

    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        lock (_records)
        {
            _records.Add(record);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Records, then hands the record to the real audit store.</summary>
internal sealed class ForwardingAuditLog(RecordingAuditLog recorder, IAuditLog inner) : IAuditLog
{
    public async Task RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        await recorder.RecordAsync(record, cancellationToken);
        await inner.RecordAsync(record, cancellationToken);
    }
}

/// <summary>Keeps every event published, with its payload as JSON.</summary>
public sealed class RecordingEvents : IEventPublisher
{
    private readonly List<(string Type, string Json)> _events = [];

    public IReadOnlyList<(string Type, string Json)> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public Task PublishAsync<T>(string eventType, T data, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        lock (_events)
        {
            _events.Add((eventType, JsonSerializer.Serialize(data, typeInfo)));
        }
        return Task.CompletedTask;
    }
}

/// <summary>The real clock plus an offset that tests move forward.</summary>
public sealed class MovableTimeProvider : TimeProvider
{
    private TimeSpan _offset;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;

    public void Advance(TimeSpan by) => _offset += by;
}
