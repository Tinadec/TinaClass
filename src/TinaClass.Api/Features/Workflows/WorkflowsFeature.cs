using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Workflows.Data;
using TinaClass.Api.Features.Workflows.Data.Sqlite;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Sample;
using TinaClass.Contracts.Workflows;

namespace TinaClass.Api.Features.Workflows;

/// <summary>/flows and /flow-runs: the workflow canvas's data, versions and runs (design 4.4; optional, off by default). Its approval node asks through <see cref="IApprovalSink"/> and listens through <see cref="IApprovalObserver"/>.</summary>
public static class WorkflowsFeature
{
    public static IServiceCollection AddWorkflowsFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFeatureDb<WorkflowsDbContext>(WorkflowsDbContext.Name)
                .WithSqlite<SqliteWorkflowsDbContext>()
                ;
        services.AddOptions<WorkflowsOptions>().Bind(configuration.GetSection("Workflows"));
        services.AddSingleton<FlowRunner>();
        services.AddScoped<FlowExecutor>();
        services.AddScoped<WorkflowsService>();
        services.AddScoped<IApprovalObserver, FlowApprovalObserver>();
        services.AddSingleton<ISampleSeeder, WorkflowsSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapWorkflowsFeature(this IEndpointRouteBuilder endpoints)
    {
        var flows = endpoints.MapGroup(ApiRoutes.Flows).WithTags("Workflows");
        flows.MapGet("", (WorkflowsService s, CancellationToken ct) => s.ListAsync(ct)).RequirePermission(Permissions.WorkflowsRun);
        flows.MapGet("/{id:guid}", (Guid id, WorkflowsService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(Permissions.WorkflowsRun);
        flows.MapPost("", async (CreateFlowRequest r, WorkflowsService s, CancellationToken ct) =>
        {
            var flow = await s.CreateAsync(r, ct);
            return Results.Created($"{ApiRoutes.Flows}/{flow.Id}", flow);
        }).RequirePermission(Permissions.WorkflowsWrite);
        flows.MapPut("/{id:guid}/draft", (Guid id, PutDraftRequest r, WorkflowsService s, CancellationToken ct) => s.PutDraftAsync(id, r, ct)).RequirePermission(Permissions.WorkflowsWrite);
        flows.MapPost("/{id:guid}/publish", (Guid id, WorkflowsService s, CancellationToken ct) => s.PublishAsync(id, ct)).RequirePermission(Permissions.WorkflowsWrite);
        flows.MapGet("/{id:guid}/versions", (Guid id, WorkflowsService s, CancellationToken ct) => s.VersionsAsync(id, ct)).RequirePermission(Permissions.WorkflowsRun);
        flows.MapDelete("/{id:guid}", async (Guid id, WorkflowsService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.WorkflowsWrite);
        flows.MapGet("/{id:guid}/runs", (Guid id, WorkflowsService s, CancellationToken ct) => s.RunsAsync(id, ct)).RequirePermission(Permissions.WorkflowsRun);
        flows.MapPost("/{id:guid}/runs", async (Guid id, StartFlowRunRequest r, WorkflowsService s, CancellationToken ct) =>
        {
            var run = await s.StartAsync(id, r, ct);
            return Results.Accepted($"{ApiRoutes.FlowRuns}/{run.Id}", run);
        }).RequirePermission(Permissions.WorkflowsRun);

        endpoints.MapGroup(ApiRoutes.FlowRuns).WithTags("Workflows").MapGet("/{id:guid}", (Guid id, WorkflowsService s, CancellationToken ct) => s.GetRunAsync(id, ct)).RequirePermission(Permissions.WorkflowsRun);
        return endpoints;
    }
}

/// <summary>The sample flow "工单自动分诊" at v12 (twelve identical published versions) and run #842 waiting at "发送前确认" (design 16).</summary>
internal sealed class WorkflowsSeeder : ISampleSeeder
{
    public int Order => 55;

    public async Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<WorkflowsDbContext>();
        if (await db.Flows.AnyAsync(cancellationToken))
        {
            return;
        }
        var json = FlowGraphs.ToJson(WorkflowsSample.Graph);
        db.Flows.Add(new Flow { Id = WorkflowsSample.FlowId, OwnerId = SampleIds.Admin, Name = "工单自动分诊", DraftJson = json, PublishedVersion = 12, UpdatedAt = sample.At(2, 14, 0) });
        for (var n = 1; n <= 12; n++)
        {
            db.Versions.Add(new FlowVersion { Id = Guid.NewGuid(), FlowId = WorkflowsSample.FlowId, Number = n, GraphJson = json, PublishedBy = n % 3 == 0 ? SampleIds.Owner : SampleIds.Admin, PublishedAt = sample.At((12 - n) * 3 + 2, 14, 0) });
        }
        var started = sample.Now.AddSeconds(-45);
        db.Runs.Add(new FlowRun { Id = SampleIds.Run(8420), FlowId = WorkflowsSample.FlowId, FlowVersion = 12, Ordinal = 842, State = FlowRunStates.Waiting, Input = "工单 #48213：导出 12 万行卡在 80%", StartedAt = started, Cost = 31 });
        // The approval is a ticket of the approvals feature, when it is there; the node remembers its id so that a decision resumes the run.
        var ticket = await services.GetRequiredService<IApprovalSink>().RequestAsync(new ApprovalRequest("flow", SampleIds.Run(8420), "发送前确认", "工单 #48213 的回复邮件", [new("flow run", "#842"), new("node", "发送前确认")], "external", null), cancellationToken);
        foreach (var node in WorkflowsSample.Run842)
        {
            db.NodeRuns.Add(new FlowNodeRun { Id = Guid.NewGuid(), RunId = SampleIds.Run(8420), NodeKey = node.Key, State = node.State, StartMs = node.StartMs, DurationMs = node.DurationMs,
                Output = node.State == NodeStates.Waiting ? (ticket is null ? null : $"approval:{ticket}") : node.Output });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
