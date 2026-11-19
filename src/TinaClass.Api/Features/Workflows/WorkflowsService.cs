using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Workflows.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Workflows;

namespace TinaClass.Api.Features.Workflows;

public static class WorkflowsAudit
{
    public const string Created = "workflows.flow_created";
    public const string DraftSaved = "workflows.draft_saved";
    public const string Deleted = "workflows.flow_deleted";
}

public static class FlowRunStates
{
    public const string Running = "running";
    public const string Waiting = "waiting";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    public static bool IsFinal(string s) => s is Succeeded or Failed;
}

public sealed class WorkflowsOptions
{
    /// <summary>The pause between nodes of a run (so that the trace can be watched); the tests set it to 0.</summary>
    public int NodeDelayMs { get; set; } = 600;
}

/// <summary>The rules of a graph: sizes, unique keys, edges between known nodes, no cycle, one trigger to publish.</summary>
public static partial class FlowGraphs
{
    public const int MaxNodes = 40;
    public const int MaxEdges = 80;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,60}$")]
    private static partial Regex KeyShape();

    private static readonly string[] Kinds = [NodeKinds.Trigger, NodeKinds.Model, NodeKinds.Retrieve, NodeKinds.Branch, NodeKinds.Tool, NodeKinds.Approval, NodeKinds.Send];

    public static void Validate(FlowGraphDto graph, bool forPublish)
    {
        var validator = new Validator();
        if (graph.Nodes is null || graph.Edges is null)
        {
            throw ApiException.Invalid("graph", "Give the nodes and the edges.");
        }
        if (graph.Nodes.Count > MaxNodes)
        {
            validator.Add("nodes", $"At most {MaxNodes} nodes.");
        }
        if (graph.Edges.Count > MaxEdges)
        {
            validator.Add("edges", $"At most {MaxEdges} edges.");
        }
        validator.ThrowIfInvalid();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (!KeyShape().IsMatch(node.Key ?? "") || !keys.Add(node.Key!))
            {
                validator.Add("nodes", "Each node needs its own key of letters, digits, - or _ (at most 60).");
            }
            if (!Kinds.Contains(node.Kind))
            {
                validator.Add("nodes", $"Unknown node kind {node.Kind}.");
            }
            if ((node.Title?.Length ?? 0) > 80 || (node.Subtitle?.Length ?? 0) > 120)
            {
                validator.Add("nodes", "A title has at most 80 characters, a subtitle 120.");
            }
            if (node.X is < -10_000 or > 10_000 || node.Y is < -10_000 or > 10_000)
            {
                validator.Add("nodes", "A node lies outside the canvas.");
            }
        }
        validator.ThrowIfInvalid();
        var pairs = new HashSet<(string, string)>();
        foreach (var edge in graph.Edges)
        {
            if (!keys.Contains(edge.From) || !keys.Contains(edge.To) || edge.From == edge.To || !pairs.Add((edge.From, edge.To)) || (edge.Label?.Length ?? 0) > 20)
            {
                validator.Add("edges", "An edge joins two different nodes of the graph, once, with a label of at most 20 characters.");
            }
        }
        validator.ThrowIfInvalid();
        if (TopologicalOrder(graph) is null)
        {
            throw ApiException.Invalid("edges", "The graph has a loop.");
        }
        if (forPublish)
        {
            var triggers = graph.Nodes.Where(n => n.Kind == NodeKinds.Trigger).ToList();
            if (triggers.Count != 1 || graph.Edges.Any(e => e.To == triggers[0].Key))
            {
                throw ApiException.Invalid("nodes", "A flow needs exactly one trigger, and nothing leads into it.");
            }
            if (graph.Nodes.Count < 2)
            {
                throw ApiException.Invalid("nodes", "A flow needs at least one node after its trigger.");
            }
        }
    }

    /// <summary>The nodes in an order where every node comes after the ones that lead to it; null when there is a loop.</summary>
    public static List<FlowNodeDto>? TopologicalOrder(FlowGraphDto graph)
    {
        var incoming = graph.Nodes.ToDictionary(n => n.Key, n => graph.Edges.Count(e => e.To == n.Key), StringComparer.Ordinal);
        var order = new List<FlowNodeDto>();
        var queue = new Queue<FlowNodeDto>(graph.Nodes.Where(n => incoming[n.Key] == 0));
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            order.Add(node);
            foreach (var edge in graph.Edges.Where(e => e.From == node.Key))
            {
                if (--incoming[edge.To] == 0)
                {
                    queue.Enqueue(graph.Nodes.First(n => n.Key == edge.To));
                }
            }
        }
        return order.Count == graph.Nodes.Count ? order : null;
    }

    public static string ToJson(FlowGraphDto graph) => JsonSerializer.Serialize(graph, ContractsJsonContext.Default.FlowGraphDto);

    public static FlowGraphDto FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ContractsJsonContext.Default.FlowGraphDto) ?? new FlowGraphDto([], []);
        }
        catch (JsonException)
        {
            return new FlowGraphDto([], []);
        }
    }
}

/// <summary>Flows, the draft, immutable published versions and runs (design 4.4). A flow is seen and run by everyone with <c>workflows.run</c>; changed with <c>workflows.write</c>.</summary>
public sealed class WorkflowsService(WorkflowsDbContext db, IActorContext actors, IUserDirectory users, FlowRunner runner, IAuditLog audit, IEventPublisher events, TimeProvider time)
{
    public async Task<IReadOnlyList<FlowDto>> ListAsync(CancellationToken cancellationToken)
    {
        var flows = await db.Flows.AsNoTracking().OrderBy(f => f.Name).ThenBy(f => f.Id).Take(200).ToListAsync(cancellationToken);
        var result = new List<FlowDto>();
        foreach (var flow in flows)
        {
            result.Add(await DtoAsync(flow, cancellationToken));
        }
        return result;
    }

    public async Task<FlowDto> GetAsync(Guid id, CancellationToken cancellationToken) => await DtoAsync(await FindAsync(id, false, cancellationToken), cancellationToken);

    public async Task<FlowDto> CreateAsync(CreateFlowRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        new Validator().Text("name", request.Name, 80).ThrowIfInvalid();
        var graph = new FlowGraphDto([new FlowNodeDto("n1", NodeKinds.Trigger, "Start", "Trigger", 30, 250)], []);
        var flow = new Flow { Id = Guid.NewGuid(), OwnerId = actor.Id ?? Guid.Empty, Name = request.Name.Trim(), DraftJson = FlowGraphs.ToJson(graph), UpdatedAt = time.GetUtcNow() };
        db.Flows.Add(flow);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(WorkflowsAudit.Created, "flow", flow.Id.ToString(), flow.Name), cancellationToken);
        return await DtoAsync(flow, cancellationToken);
    }

    public async Task<FlowDto> PutDraftAsync(Guid id, PutDraftRequest request, CancellationToken cancellationToken)
    {
        var flow = await FindAsync(id, true, cancellationToken);
        FlowGraphs.Validate(request.Graph, forPublish: false);
        var json = FlowGraphs.ToJson(request.Graph);
        if (json.Length > WorkflowsDbContext.MaxGraph)
        {
            throw ApiException.Invalid("graph", "The graph is too large.");
        }
        db.ExpectVersion(flow, request.Version);
        flow.DraftJson = json;
        flow.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(WorkflowsAudit.DraftSaved, "flow", id.ToString(), flow.Name), cancellationToken);
        return await DtoAsync(flow, cancellationToken);
    }

    /// <summary>Publishing copies the draft into a new immutable version (v12, v13, …); runs use the latest published one.</summary>
    public async Task<FlowDto> PublishAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var flow = await FindAsync(id, true, cancellationToken);
        FlowGraphs.Validate(FlowGraphs.FromJson(flow.DraftJson), forPublish: true);
        flow.PublishedVersion++;
        flow.UpdatedAt = time.GetUtcNow();
        db.Versions.Add(new FlowVersion { Id = Guid.NewGuid(), FlowId = id, Number = flow.PublishedVersion, GraphJson = flow.DraftJson, PublishedBy = actor.Id ?? Guid.Empty, PublishedAt = flow.UpdatedAt });
        await db.SaveChangesAsync(cancellationToken);
        var dto = await DtoAsync(flow, cancellationToken);
        await audit.RecordAsync(new AuditRecord(WorkflowsEvents.Published, "flow", id.ToString(), flow.Name), cancellationToken);
        await events.PublishAsync(WorkflowsEvents.Published, dto with { Draft = new FlowGraphDto([], []) }, ContractsJsonContext.Default.FlowDto, cancellationToken);
        return dto;
    }

    public async Task<IReadOnlyList<FlowVersionDto>> VersionsAsync(Guid id, CancellationToken cancellationToken)
    {
        await FindAsync(id, false, cancellationToken);
        var versions = await db.Versions.AsNoTracking().Where(v => v.FlowId == id).OrderByDescending(v => v.Number).ThenBy(v => v.Id).Take(100).ToListAsync(cancellationToken);
        var names = (await users.ListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
        return [.. versions.Select(v => new FlowVersionDto(v.Id, v.Number, names.GetValueOrDefault(v.PublishedBy, ""), v.PublishedAt))];
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var flow = await FindAsync(id, true, cancellationToken);
        if (await db.Runs.AnyAsync(r => r.FlowId == id && (r.State == FlowRunStates.Running || r.State == FlowRunStates.Waiting), cancellationToken))
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "The flow has a run in progress.");
        }
        db.Flows.Remove(flow);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(WorkflowsAudit.Deleted, "flow", id.ToString(), flow.Name), cancellationToken);
    }

    public async Task<FlowRunDto> StartAsync(Guid id, StartFlowRunRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var flow = await FindAsync(id, false, cancellationToken);
        new Validator().Text("input", request.Input, 4_000, required: false).ThrowIfInvalid();
        if (flow.PublishedVersion == 0)
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "Publish the flow before running it.");
        }
        var version = await db.Versions.AsNoTracking().SingleAsync(v => v.FlowId == id && v.Number == flow.PublishedVersion, cancellationToken);
        var graph = FlowGraphs.FromJson(version.GraphJson);
        var run = new FlowRun { Id = Guid.NewGuid(), FlowId = id, FlowVersion = version.Number, State = FlowRunStates.Running, Input = request.Input?.Trim() ?? "", StartedBy = actor.Id, StartedAt = time.GetUtcNow() };
        for (var attempt = 0; ; attempt++)
        {
            run.Ordinal = (await db.Runs.Select(r => (int?)r.Ordinal).MaxAsync(cancellationToken) ?? 0) + 1;
            db.Runs.Add(run);
            db.NodeRuns.AddRange(graph.Nodes.Select(n => new FlowNodeRun { Id = Guid.NewGuid(), RunId = run.Id, NodeKey = n.Key }));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // Two runs took the same number at once: the unique index decides, this one takes the next.
                db.ChangeTracker.Clear();
            }
        }
        runner.Launch(run.Id);
        return await RunDtoAsync(run, cancellationToken);
    }

    public async Task<FlowRunDto> GetRunAsync(Guid id, CancellationToken cancellationToken) =>
        await RunDtoAsync(await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw ApiException.NotFound("The run"), cancellationToken);

    public async Task<IReadOnlyList<FlowRunDto>> RunsAsync(Guid flowId, CancellationToken cancellationToken)
    {
        await FindAsync(flowId, false, cancellationToken);
        var runs = await db.Runs.AsNoTracking().Where(r => r.FlowId == flowId).OrderByDescending(r => r.StartedAt).ThenBy(r => r.Id).Take(30).ToListAsync(cancellationToken);
        var result = new List<FlowRunDto>();
        foreach (var run in runs)
        {
            result.Add(await RunDtoAsync(run, cancellationToken));
        }
        return result;
    }

    private async Task<Flow> FindAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? db.Flows.AsQueryable() : db.Flows.AsNoTracking();
        return await query.SingleOrDefaultAsync(f => f.Id == id, cancellationToken) ?? throw ApiException.NotFound("The flow");
    }

    private async Task<FlowDto> DtoAsync(Flow flow, CancellationToken cancellationToken)
    {
        var published = flow.PublishedVersion == 0 ? null : await db.Versions.AsNoTracking().Where(v => v.FlowId == flow.Id && v.Number == flow.PublishedVersion).Select(v => v.GraphJson).SingleOrDefaultAsync(cancellationToken);
        return new FlowDto(flow.Id, flow.Name, flow.PublishedVersion, FlowGraphs.FromJson(flow.DraftJson), published != flow.DraftJson, flow.UpdatedAt, flow.Version);
    }

    private async Task<FlowRunDto> RunDtoAsync(FlowRun run, CancellationToken cancellationToken)
    {
        var nodes = await db.NodeRuns.AsNoTracking().Where(n => n.RunId == run.Id).ToListAsync(cancellationToken);
        var order = (await db.Versions.AsNoTracking().Where(v => v.FlowId == run.FlowId && v.Number == run.FlowVersion).Select(v => v.GraphJson).SingleOrDefaultAsync(cancellationToken)) is { } json
            ? FlowGraphs.FromJson(json).Nodes.Select(n => n.Key).ToList() : [];
        return new FlowRunDto(run.Id, run.FlowId, run.FlowVersion, run.State, run.StartedAt, run.EndedAt, run.Cost, run.Ordinal,
            [.. nodes.OrderBy(n => order.IndexOf(n.NodeKey) is var i && i < 0 ? int.MaxValue : i).ThenBy(n => n.NodeKey, StringComparer.Ordinal).Select(n => new FlowNodeRunDto(n.NodeKey, n.State, n.StartMs, n.DurationMs, n.Output))]);
    }
}

/// <summary>Starts and resumes flow runs, each on its own in its own scope.</summary>
public sealed class FlowRunner(IServiceScopeFactory scopes)
{
    private readonly ConcurrentDictionary<Guid, Task> _active = new();

    public void Launch(Guid runId)
    {
        var gate = new TaskCompletionSource();
        if (!_active.TryAdd(runId, gate.Task))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FlowExecutor>().ExecuteAsync(runId, CancellationToken.None);
            }
            finally
            {
                _active.TryRemove(runId, out _);
                gate.SetResult();
            }
        });
    }

    /// <summary>Waits until the run's executor has stopped (finished, or waiting for an approval).</summary>
    public Task WhenIdleAsync(Guid runId) => _active.TryGetValue(runId, out var task) ? task : Task.CompletedTask;
}
