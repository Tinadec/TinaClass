using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.Api.Features.Agents;
using TinaClass.Api.Features.Agents.Data;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Agents;

/// <summary>Remembers which tools an agent's run really executed, so that a test can show that an unapproved one was not.</summary>
public sealed class SpyExecutor : IToolExecutor
{
    private readonly ConcurrentQueue<string> _calls = new();

    public IReadOnlyList<string> Calls => [.. _calls];

    public Task<ToolOutcome> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        _calls.Enqueue(call.Name);
        return Task.FromResult(new ToolOutcome("spy"));
    }
}

/// <summary>A tool server over HTTP: answers the MCP <c>tools/list</c> call with <see cref="Tools"/>, or fails.</summary>
public sealed class StubMcp : HttpMessageHandler
{
    public List<(string Name, string Description)> Tools { get; } = [];

    /// <summary><c>ok</c>, <c>500</c> or <c>down</c> (the connection fails).</summary>
    public string Mode { get; set; } = "ok";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Mode == "down")
        {
            throw new HttpRequestException("connection refused");
        }
        if (Mode == "500")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
        var list = string.Join(',', Tools.Select(t => $"{{\"name\":\"{t.Name}\",\"description\":\"{t.Description}\"}}"));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{{\"tools\":[{list}]}}}}", Encoding.UTF8, "application/json") });
    }
}

/// <summary>
/// The API with a spy in place of the tool executor and a stub MCP server behind the discovery client. It shares the database and the signing
/// key of the fixture's own server, so users made through the fixture sign in here.
/// </summary>
public abstract class AgentsTestBase(ApiFactory api) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected ApiFactory Api => api;

    protected SpyExecutor Spy { get; } = new();

    protected StubMcp Mcp { get; } = new();

    protected WebApplicationFactory<Program> Host { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        Host = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IToolExecutor>();
            services.AddSingleton<IToolExecutor>(Spy);
            services.AddHttpClient(ToolDiscovery.ClientName).ConfigurePrimaryHttpMessageHandler(() => Mcp);
        }));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();

    protected async Task<HttpClient> ClientAsync(string role) => await SignInAsync((await api.CreateUserAsync(role)).Email);

    protected async Task<HttpClient> SignInAsync(string email)
    {
        var tokens = await api.LoginAsync(email);
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    protected async Task<Tool> SeedToolAsync(string risk, string? name = null, string serverKind = "mcp", string serverStatus = "ok")
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentsDbContext>();
        var server = new ToolServer { Id = Guid.NewGuid(), Name = "Server " + Guid.NewGuid().ToString("N")[..6], Kind = serverKind, Endpoint = serverKind == "builtin" ? "builtin://tools" : "https://srv.example/mcp", Status = serverStatus };
        var tool = new Tool { Id = Guid.NewGuid(), ServerId = server.Id, Name = name ?? $"{risk}-{Guid.NewGuid():N}"[..14], Risk = risk };
        db.Servers.Add(server);
        db.Tools.Add(tool);
        await db.SaveChangesAsync(Ct);
        return tool;
    }

    protected static AgentToolSetting Use(Tool tool, string approval = ToolApprovals.Auto, bool enabled = true) => new(tool.Id, enabled, approval);

    protected async Task<AgentDto> NewAgentAsync(HttpClient client, IReadOnlyList<AgentToolSetting>? tools = null, int maxSteps = 20, string visibility = "workspace", bool publish = true, string instruction = "Help with the task.")
    {
        var created = await Expect.JsonAsync<AgentDto>(await client.PostJsonAsync(ApiRoutes.Agents,
            new SaveAgentRequest("Agent " + Guid.NewGuid().ToString("N")[..6], "", instruction, "", maxSteps, 0, visibility, null, null, tools ?? [], [], 0)), HttpStatusCode.Created);
        return publish ? await Expect.JsonAsync<AgentDto>(await client.PostAsync($"{ApiRoutes.Agents}/{created.Id}/publish", null, Ct)) : created;
    }

    /// <summary>Starts a run and waits until it is finished or waits for a person.</summary>
    protected async Task<RunDto> RunAsync(HttpClient client, AgentDto agent, string subject = "Check the export problem")
    {
        var started = await Expect.JsonAsync<RunDto>(await client.PostJsonAsync($"{ApiRoutes.Agents}/{agent.Id}/runs", new StartRunRequest(subject)), HttpStatusCode.Accepted);
        return await IdleAsync(client, started.Id);
    }

    protected async Task<RunDto> IdleAsync(HttpClient client, Guid runId)
    {
        await Host.Services.GetRequiredService<RunRegistry>().WhenIdleAsync(runId).WaitAsync(TimeSpan.FromSeconds(20), Ct);
        return await client.GetJsonAsync<RunDto>($"{ApiRoutes.Runs}/{runId}");
    }

    protected async Task<IReadOnlyList<ApprovalDto>> PendingAsync(HttpClient approver, Guid runId) =>
        [.. (await approver.GetJsonAsync<IReadOnlyList<ApprovalDto>>(ApiRoutes.Approvals + "?state=pending")).Where(a => a.RunId == runId)];

    protected Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid approvalId, bool approve, string? reason = null) =>
        client.PostJsonAsync($"{ApiRoutes.Approvals}/{approvalId}/{(approve ? "approve" : "reject")}", new DecideApprovalRequest(reason));
}
