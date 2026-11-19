using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.Api.Common;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Agents.Data;
using TinaClass.Api.Features.Agents.Data.Sqlite;
using TinaClass.Contracts;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Sample;

namespace TinaClass.Api.Features.Agents;

/// <summary>/agents, /runs, /approvals, /tool-servers and /tools (design 4.3). Provides <see cref="IToolCatalog"/> and <see cref="IApprovalSink"/> to chat and the workflows.</summary>
public static class AgentsFeature
{
    public static IServiceCollection AddAgentsFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFeatureDb<AgentsDbContext>(AgentsDbContext.Name)
                .WithSqlite<SqliteAgentsDbContext>()
                ;
        services.AddOptions<AgentsOptions>().Bind(configuration.GetSection("Agents"));
        services.AddHttpClient(ToolDiscovery.ClientName);
        services.AddSingleton<RunRegistry>();
        services.AddSingleton<IToolExecutor, SimulatedToolExecutor>();
        services.AddScoped<ToolDiscovery>();
        services.AddScoped<AgentsService>();
        services.AddScoped<RunService>();
        services.AddScoped<RunLauncher>();
        services.AddScoped<RunExecutor>();
        services.Replace(ServiceDescriptor.Scoped<IToolCatalog, AgentToolCatalog>());
        services.Replace(ServiceDescriptor.Scoped<IApprovalSink, ApprovalSink>());
        services.AddSingleton<ISampleSeeder, AgentsSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapAgentsFeature(this IEndpointRouteBuilder endpoints)
    {
        var agents = endpoints.MapGroup(ApiRoutes.Agents).WithTags("Agents");
        agents.MapGet("", (AgentsService s, CancellationToken ct) => s.ListAsync(ct)).RequirePermission(Permissions.AgentsUse);
        agents.MapGet("/{id:guid}", (Guid id, AgentsService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(Permissions.AgentsUse);
        agents.MapPost("", async (SaveAgentRequest r, AgentsService s, CancellationToken ct) =>
        {
            var agent = await s.CreateAsync(r, ct);
            return Results.Created($"{ApiRoutes.Agents}/{agent.Id}", agent);
        }).RequirePermission(Permissions.AgentsWrite);
        agents.MapPatch("/{id:guid}", (Guid id, SaveAgentRequest r, AgentsService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(Permissions.AgentsWrite);
        agents.MapDelete("/{id:guid}", async (Guid id, AgentsService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AgentsWrite);
        agents.MapPost("/{id:guid}/publish", (Guid id, AgentsService s, CancellationToken ct) => s.PublishAsync(id, ct)).RequirePermission(Permissions.AgentsWrite);
        agents.MapPost("/{id:guid}/runs", async (Guid id, StartRunRequest r, RunService s, CancellationToken ct) =>
        {
            var run = await s.StartAsync(id, r, "manual", ct);
            return Results.Accepted($"{ApiRoutes.Runs}/{run.Id}", run);
        }).RequirePermission(Permissions.AgentsUse);

        var runs = endpoints.MapGroup(ApiRoutes.Runs).WithTags("Agents");
        runs.MapGet("", (Guid? agentId, string? state, int? page, int? pageSize, RunService s, CancellationToken ct) => s.ListAsync(agentId, state, page, pageSize, ct)).RequirePermission(Permissions.AgentsUse);
        runs.MapGet("/{id:guid}", (Guid id, RunService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(Permissions.AgentsUse);
        runs.MapPost("/{id:guid}/cancel", (Guid id, RunService s, CancellationToken ct) => s.CancelAsync(id, ct)).RequirePermission(Permissions.AgentsUse);
        runs.MapGet("/{id:guid}/events", async (Guid id, HttpContext http, RunService s, CancellationToken ct) =>
        {
            // The run is looked up before the first byte, so that a missing one is a 404 problem and not a broken stream.
            await using var events = s.WatchAsync(id, ct).GetAsyncEnumerator(ct);
            var any = await events.MoveNextAsync();
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            try
            {
                var n = 0;
                while (any)
                {
                    await http.Response.WriteAsync($"id: {++n}\nevent: {events.Current.Event}\ndata: {events.Current.Json}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                    any = await events.MoveNextAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // The client went away; the run goes on.
            }
        }).RequirePermission(Permissions.AgentsUse);

        var approvals = endpoints.MapGroup(ApiRoutes.Approvals).WithTags("Agents");
        approvals.MapGet("", (string? state, RunService s, CancellationToken ct) => s.ApprovalsAsync(state, ct)).RequirePermission(Permissions.AgentsApprove);
        approvals.MapPost("/{id:guid}/approve", (Guid id, DecideApprovalRequest r, RunService s, CancellationToken ct) => s.ApproveAsync(id, r, ct)).RequirePermission(Permissions.AgentsApprove);
        approvals.MapPost("/{id:guid}/reject", (Guid id, DecideApprovalRequest r, RunService s, CancellationToken ct) => s.RejectAsync(id, r, ct)).RequirePermission(Permissions.AgentsApprove);

        var servers = endpoints.MapGroup(ApiRoutes.ToolServers).WithTags("Agents");
        servers.MapGet("", (AgentsService s, CancellationToken ct) => s.ServersAsync(ct)).RequirePermission(Permissions.ToolsManage);
        servers.MapPost("", async (CreateToolServerRequest r, AgentsService s, CancellationToken ct) =>
        {
            var server = await s.AddServerAsync(r, ct);
            return Results.Created($"{ApiRoutes.ToolServers}/{server.Id}", server);
        }).RequirePermission(Permissions.ToolsManage);
        servers.MapDelete("/{id:guid}", async (Guid id, AgentsService s, CancellationToken ct) =>
        {
            await s.DeleteServerAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ToolsManage);
        servers.MapPost("/{id:guid}/refresh", (Guid id, AgentsService s, CancellationToken ct) => s.RefreshAsync(id, ct)).RequirePermission(Permissions.ToolsManage);

        var tools = endpoints.MapGroup(ApiRoutes.Tools).WithTags("Agents");
        tools.MapGet("", (AgentsService s, CancellationToken ct) => s.ToolsAsync(ct)).RequirePermission(Permissions.AgentsUse);
        tools.MapPatch("/{id:guid}", (Guid id, UpdateToolRequest r, AgentsService s, CancellationToken ct) => s.UpdateToolAsync(id, r, ct)).RequirePermission(Permissions.ToolsManage);
        return endpoints;
    }
}

/// <summary>
/// The sample agents, servers and tools (design 16) and run 842 of the ticket triage agent: seven steps, the last one waiting for the approval to send an email
/// (a pending approval, <see cref="SampleIds.Approval"/> 1). Approving it lets the run finish.
/// </summary>
internal sealed class AgentsSeeder : ISampleSeeder
{
    public int Order => 50;

    public async Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AgentsDbContext>();
        if (await db.Servers.AnyAsync(cancellationToken))
        {
            return;
        }
        foreach (var s in AgentsSample.Servers)
        {
            db.Servers.Add(new ToolServer { Id = s.Id, Name = s.Name, Kind = s.Kind, Endpoint = s.Endpoint, Status = s.Status, LastSeenAt = s.Status == "ok" ? sample.Now.AddMinutes(-3) : sample.Now.AddHours(-5) });
        }
        foreach (var (t, i) in AgentsSample.Tools.Select((t, i) => (t, i)))
        {
            db.Tools.Add(new Tool { Id = t.Id, ServerId = t.ServerId, Name = t.Name, Description = t.Description, Risk = t.Risk, Sort = i });
        }
        var owners = new[] { SampleIds.Owner, SampleIds.Admin, SampleIds.He, SampleIds.Zhou, SampleIds.Tang, SampleIds.Owner };
        foreach (var (a, i) in AgentsSample.Agents.Select((a, i) => (a, i)))
        {
            db.Agents.Add(new Agent
            {
                Id = a.Id, OwnerId = owners[i], Name = a.Name, Description = a.Description, Instruction = a.Instruction, ModelCode = a.ModelCode, MaxSteps = a.MaxSteps, MaxCost = a.MaxCost, Visibility = "workspace",
                State = "published", Schedule = a.Schedule, Icon = a.Icon, RunCount = a.Runs, SuccessCount = (int)Math.Round(a.Runs * a.SuccessPercent / 100.0), UpdatedAt = sample.At(i + 1, 11, 0),
            });
            db.AgentTools.AddRange(a.ToolIds.Select((tool, n) => new AgentToolLink { Id = Guid.NewGuid(), AgentId = a.Id, ToolId = tool, Enabled = true, Approval = a.Approvals[n], Sort = n }));
        }
        // The triage agent reads the customer-service base.
        db.AgentBases.Add(new AgentBaseLink { Id = Guid.NewGuid(), AgentId = SampleIds.Agent(2), BaseId = SampleIds.Base(3) });

        var triage = AgentsSample.Agents[1];
        var started = sample.Now.AddSeconds(-AgentsSample.Run842[^1].AtMs / 1000.0 - 40);
        var run = new AgentRun { Id = SampleIds.Run(842), AgentId = triage.Id, StartedBy = null, Trigger = "webhook", Subject = "工单 #48213：导出 12 万行卡在 80%", State = RunStates.WaitingApproval, StartedAt = started, Tokens = 2_840, Cost = 31,
            Cursor = Array.IndexOf(triage.ToolIds, SampleIds.Tool(6)) };
        db.Runs.Add(run);
        Guid? approvalStep = null;
        foreach (var (s, i) in AgentsSample.Run842.Select((s, i) => (s, i)))
        {
            var step = new RunStep { Id = Guid.NewGuid(), RunId = run.Id, Ordinal = i, Kind = s.Kind, Title = s.Title, Detail = s.Detail, State = s.State, AtMs = s.AtMs };
            db.Steps.Add(step);
            approvalStep = s.Kind == "approval" ? step.Id : approvalStep;
        }
        db.Approvals.Add(new AgentApproval
        {
            Id = SampleIds.Approval(1), Source = "agent", SourceId = triage.Id, RunId = run.Id, ToolIndex = run.Cursor, StepId = approvalStep, Title = "发送邮件", Detail = "对外发送需要你确认",
            ArgumentsJson = ApprovalArguments.ToJson(AgentsSample.ApprovalArguments), Risk = ToolRisks.External, RequestedBy = null, CreatedAt = started.AddSeconds(18),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
