using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Workflows.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Workflows;

namespace TinaClass.Api.Features.Workflows;

/// <summary>
/// Carries a run out over the published graph: a node runs once everything leading into it has finished and at least one edge into it is active
/// (a branch activates only the edge with its choice); a node none of whose edges is active is skipped. An approval node asks a person (through
/// <see cref="IApprovalSink"/>) and the run waits; <see cref="FlowApprovalObserver"/> resumes it. Model nodes call the model gateway, retrieval nodes the
/// knowledge retriever; tool and send nodes are simulated (nothing leaves the API).
/// </summary>
public sealed class FlowExecutor(WorkflowsDbContext db, IKnowledgeRetriever retriever, IModelCatalog models, IModelGateway gateway, IBudgetGuard budget, IUsageRecorder usage, IApprovalSink sink,
    IEventPublisher events, IOptions<WorkflowsOptions> options, TimeProvider time)
{
    private static readonly string[] DefectWords = ["缺陷", "bug", "error", "fail", "故障", "p0", "p1", "卡住", "超时"];

    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await db.Runs.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null || FlowRunStates.IsFinal(run.State))
        {
            return;
        }
        try
        {
            var graph = FlowGraphs.FromJson(await db.Versions.AsNoTracking().Where(v => v.FlowId == run.FlowId && v.Number == run.FlowVersion).Select(v => v.GraphJson).SingleAsync(cancellationToken));
            var order = FlowGraphs.TopologicalOrder(graph) ?? throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.InvalidState, "The graph has a loop.");
            run.State = FlowRunStates.Running;
            await db.SaveChangesAsync(cancellationToken);
            var nodeRuns = await db.NodeRuns.Where(n => n.RunId == runId).ToDictionaryAsync(n => n.NodeKey, StringComparer.Ordinal, cancellationToken);
            var progressed = true;
            while (progressed)
            {
                progressed = false;
                foreach (var node in order)
                {
                    var nodeRun = nodeRuns[node.Key];
                    if (nodeRun.State != NodeStates.Pending)
                    {
                        continue;
                    }
                    var incoming = graph.Edges.Where(e => e.To == node.Key).ToList();
                    if (incoming.Any(e => nodeRuns[e.From].State is NodeStates.Pending or NodeStates.Running or NodeStates.Waiting))
                    {
                        continue;
                    }
                    var active = incoming.Where(e => IsActive(e, graph, nodeRuns)).ToList();
                    if (incoming.Count > 0 && active.Count == 0)
                    {
                        nodeRun.State = NodeStates.Skipped;
                        await db.SaveChangesAsync(cancellationToken);
                        progressed = true;
                        continue;
                    }
                    var context = incoming.Count == 0 ? run.Input : string.Join("\n", active.Select(e => nodeRuns[e.From].Output).Where(o => !string.IsNullOrEmpty(o)));
                    var waiting = await RunNodeAsync(run, node, nodeRun, graph, context, cancellationToken);
                    progressed = true;
                    if (waiting)
                    {
                        run.State = FlowRunStates.Waiting;
                        await db.SaveChangesAsync(cancellationToken);
                        return;
                    }
                    if (nodeRun.State == NodeStates.Failed)
                    {
                        await FinishAsync(run, nodeRuns, FlowRunStates.Failed, nodeRun.Output, cancellationToken);
                        return;
                    }
                    if (options.Value.NodeDelayMs > 0)
                    {
                        await Task.Delay(options.Value.NodeDelayMs, cancellationToken);
                    }
                }
            }
            // A node that failed while the run was waiting (a rejected approval) fails the run.
            var failed = nodeRuns.Values.FirstOrDefault(n => n.State == NodeStates.Failed);
            await FinishAsync(run, nodeRuns, failed is null ? FlowRunStates.Succeeded : FlowRunStates.Failed, failed?.Output, cancellationToken);
        }
        catch (ApiException exception)
        {
            await FailAsync(run, exception.Message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await FailAsync(run, "The run failed.", cancellationToken);
        }
    }

    private static bool IsActive(FlowEdgeDto edge, FlowGraphDto graph, Dictionary<string, FlowNodeRun> nodeRuns)
    {
        var source = nodeRuns[edge.From];
        if (source.State != NodeStates.Succeeded)
        {
            return false;
        }
        return graph.Nodes.First(n => n.Key == edge.From).Kind != NodeKinds.Branch || edge.Label is null || edge.Label == source.Output;
    }

    /// <summary>Runs one node; true when it has to wait for an approval.</summary>
    private async Task<bool> RunNodeAsync(FlowRun run, FlowNodeDto node, FlowNodeRun nodeRun, FlowGraphDto graph, string context, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        nodeRun.State = NodeStates.Running;
        nodeRun.StartMs = (int)Math.Min((time.GetUtcNow() - run.StartedAt).TotalMilliseconds, int.MaxValue);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            switch (node.Kind)
            {
                case NodeKinds.Trigger:
                    nodeRun.Output = Cut(run.Input, 2_000);
                    break;
                case NodeKinds.Model:
                    nodeRun.Output = Cut(await CompleteAsync(run, node, context, cancellationToken), 2_000);
                    break;
                case NodeKinds.Retrieve:
                    var found = await retriever.SearchAsync(context, [], 4, cancellationToken);
                    nodeRun.Output = $"{found.Count} passages";
                    break;
                case NodeKinds.Branch:
                    var labels = graph.Edges.Where(e => e.From == node.Key && e.Label is not null).Select(e => e.Label!).Distinct().ToList();
                    var yes = labels.FirstOrDefault(l => l is "是" or "yes" or "true") ?? labels.FirstOrDefault();
                    var no = labels.FirstOrDefault(l => l != yes);
                    var defect = DefectWords.Any(w => context.Contains(w, StringComparison.OrdinalIgnoreCase));
                    nodeRun.Output = defect ? yes : no ?? yes;
                    break;
                case NodeKinds.Approval:
                    var ticket = await sink.RequestAsync(new ApprovalRequest("flow", run.Id, node.Title, Cut(context, 500), [new("flow run", $"#{run.Ordinal}"), new("node", node.Title)], "external", null), cancellationToken);
                    if (ticket is null)
                    {
                        nodeRun.State = NodeStates.Failed;
                        nodeRun.Output = "Approval is needed; nothing was run.";
                        break;
                    }
                    nodeRun.State = NodeStates.Waiting;
                    nodeRun.Output = $"approval:{ticket}";
                    await db.SaveChangesAsync(cancellationToken);
                    return true;
                case NodeKinds.Send:
                    nodeRun.Output = "Sent (sample: nothing leaves the API).";
                    break;
                default:
                    nodeRun.Output = "Done (sample).";
                    break;
            }
            if (nodeRun.State == NodeStates.Running)
            {
                nodeRun.State = NodeStates.Succeeded;
            }
        }
        catch (ApiException exception)
        {
            nodeRun.State = NodeStates.Failed;
            nodeRun.Output = Cut(exception.Message, 2_000);
        }
        nodeRun.DurationMs = (int)clock.ElapsedMilliseconds;
        await db.SaveChangesAsync(cancellationToken);
        return false;
    }

    private async Task<string> CompleteAsync(FlowRun run, FlowNodeDto node, string context, CancellationToken cancellationToken)
    {
        await budget.EnsureAsync(run.StartedBy, cancellationToken);
        var model = await models.ResolveAsync(null, cancellationToken) ?? throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.NoModel, "No model is enabled.");
        var clock = Stopwatch.StartNew();
        var result = await gateway.CompleteAsync(models, model.Code, [new ModelMessage("system", node.Title + (string.IsNullOrWhiteSpace(node.Subtitle) ? "" : ". " + node.Subtitle)), new ModelMessage("user", context)], cancellationToken);
        run.Cost += result.Cost;
        await usage.RecordAsync(new UsageEntry(run.StartedBy, model.Id, model.ProviderId, model.DisplayName, "flow", run.Id, result.TokensIn, result.TokensOut, 0, 0, (int)clock.ElapsedMilliseconds, result.Cost,
            $"{{\"in\":{model.PriceIn},\"out\":{model.PriceOut},\"cache\":{model.PriceCache}}}"), cancellationToken);
        return result.Text;
    }

    private async Task FinishAsync(FlowRun run, Dictionary<string, FlowNodeRun> nodeRuns, string state, string? error, CancellationToken cancellationToken)
    {
        foreach (var node in nodeRuns.Values.Where(n => n.State is NodeStates.Pending or NodeStates.Waiting && state == FlowRunStates.Failed))
        {
            node.State = NodeStates.Skipped;
        }
        run.State = state;
        run.EndedAt = time.GetUtcNow();
        run.Error = error is null ? null : Cut(error, 500);
        await db.SaveChangesAsync(cancellationToken);
        var dto = new FlowRunDto(run.Id, run.FlowId, run.FlowVersion, run.State, run.StartedAt, run.EndedAt, run.Cost, run.Ordinal,
            [.. nodeRuns.Values.Select(n => new FlowNodeRunDto(n.NodeKey, n.State, n.StartMs, n.DurationMs, null))]);
        await events.PublishAsync(WorkflowsEvents.RunCompleted, dto, ContractsJsonContext.Default.FlowRunDto, cancellationToken);
    }

    private async Task FailAsync(FlowRun run, string message, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var current = await db.Runs.SingleAsync(r => r.Id == run.Id, cancellationToken);
        var nodeRuns = await db.NodeRuns.Where(n => n.RunId == run.Id).ToDictionaryAsync(n => n.NodeKey, StringComparer.Ordinal, cancellationToken);
        current.Cost = run.Cost;
        await FinishAsync(current, nodeRuns, FlowRunStates.Failed, message, cancellationToken);
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}

/// <summary>Told when a person decides an approval: for a flow's approval node, approve lets the run go on, reject fails the node and the run.</summary>
internal sealed class FlowApprovalObserver(WorkflowsDbContext db, FlowRunner runner) : IApprovalObserver
{
    public async Task OnDecidedAsync(ApprovalDecision decision, CancellationToken cancellationToken)
    {
        if (decision.Source != "flow")
        {
            return;
        }
        var marker = $"approval:{decision.Id}";
        var node = await db.NodeRuns.SingleOrDefaultAsync(n => n.RunId == decision.SourceId && n.State == NodeStates.Waiting && n.Output == marker, cancellationToken);
        if (node is null)
        {
            return;
        }
        node.State = decision.Approved ? NodeStates.Succeeded : NodeStates.Failed;
        node.Output = decision.Approved ? "Approved." : "Rejected" + (string.IsNullOrWhiteSpace(decision.Reason) ? "." : ": " + decision.Reason);
        await db.SaveChangesAsync(cancellationToken);
        await db.Runs.Where(r => r.Id == decision.SourceId && r.State == FlowRunStates.Waiting).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, FlowRunStates.Running), cancellationToken);
        runner.Launch(decision.SourceId);
    }
}
