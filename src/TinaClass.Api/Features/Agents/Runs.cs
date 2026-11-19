using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Agents.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Features.Agents;

public sealed class AgentsOptions
{
    /// <summary>The pause between a run's steps (so that a person can watch it); the tests set it to 0.</summary>
    public int StepDelayMs { get; set; } = 700;
}

/// <summary>What a tool call is asked to do and what came back.</summary>
public sealed record ToolCall(Guid ToolId, string Name, string Risk, string ServerKind, string Subject);

public sealed record ToolOutcome(string Summary);

/// <summary>
/// Runs a tool for an agent. Only called for a tool that needs no approval or whose approval was given. The default simulates: it answers with a
/// sample result and calls nothing outside; replace it to call the tool's MCP server (the approval rules in <see cref="RunExecutor"/> stay the same).
/// </summary>
public interface IToolExecutor
{
    Task<ToolOutcome> ExecuteAsync(ToolCall call, CancellationToken cancellationToken);
}

public sealed class SimulatedToolExecutor : IToolExecutor
{
    public Task<ToolOutcome> ExecuteAsync(ToolCall call, CancellationToken cancellationToken) => Task.FromResult(new ToolOutcome($"Sample result: {call.Name} was not really called."));
}

/// <summary>The runs in this process: which are being executed and how to cancel or wait for them.</summary>
public sealed class RunRegistry
{
    private readonly ConcurrentDictionary<Guid, (CancellationTokenSource Cancel, Task Done)> _active = new();

    internal bool IsActive(Guid runId) => _active.ContainsKey(runId);

    internal void Add(Guid runId, CancellationTokenSource cancel, Task done) => _active[runId] = (cancel, done);

    internal void Remove(Guid runId)
    {
        if (_active.TryRemove(runId, out var entry))
        {
            entry.Cancel.Dispose();
        }
    }

    internal void Cancel(Guid runId)
    {
        if (_active.TryGetValue(runId, out var entry))
        {
            entry.Cancel.Cancel();
        }
    }

    /// <summary>Waits until the run's executor has stopped (finished, or waiting for an approval).</summary>
    public Task WhenIdleAsync(Guid runId) => _active.TryGetValue(runId, out var entry) ? entry.Done : Task.CompletedTask;
}

/// <summary>Starts, resumes and cancels runs. Each run is executed on its own, in its own scope, so that closing the browser does not stop it.</summary>
public sealed class RunLauncher(RunRegistry registry, IServiceScopeFactory scopes)
{
    public void Launch(Guid runId)
    {
        if (registry.IsActive(runId))
        {
            return;
        }
        var cancel = new CancellationTokenSource();
        // The run waits at the gate until it is in the registry: a very short run must not remove itself before it was added.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = Task.Run(async () =>
        {
            await gate.Task;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RunExecutor>().ExecuteAsync(runId, cancel.Token);
            }
            finally
            {
                registry.Remove(runId);
            }
        });
        registry.Add(runId, cancel, done);
        gate.SetResult();
    }
}

/// <summary>
/// Carries a run out: it thinks, searches the agent's knowledge bases, deals with each enabled tool in order, then writes the reply. A tool that
/// writes (when the agent says ask) or reaches outside (always) first becomes an approval and the run stops until a person decides; the run
/// then continues from the same tool (<see cref="AgentRun.Cursor"/>), so a restart loses nothing. A tool that was not approved never runs.
/// </summary>
public sealed class RunExecutor(AgentsDbContext db, IKnowledgeRetriever retriever, IModelCatalog models, IModelGateway gateway, IBudgetGuard budget, IUsageRecorder usage, IToolExecutor executor,
    IEventPublisher events, Microsoft.Extensions.Options.IOptions<AgentsOptions> options, TimeProvider time)
{
    public static bool NeedsApproval(string risk, string approval) =>
        approval == ToolApprovals.Always || risk == ToolRisks.External || (risk == ToolRisks.Write && approval == ToolApprovals.Ask);

    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await db.Runs.SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null || RunStates.IsFinal(run.State))
        {
            return;
        }
        var agent = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == run.AgentId, cancellationToken);
        try
        {
            await ContinueAsync(run, agent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(run, agent, RunStates.Cancelled, "Cancelled.", CancellationToken.None);
        }
        catch (ApiException exception)
        {
            await FinishAsync(run, agent, RunStates.Failed, exception.Message, CancellationToken.None);
        }
        catch (Exception)
        {
            await FinishAsync(run, agent, RunStates.Failed, "The run failed.", CancellationToken.None);
        }
    }

    private async Task ContinueAsync(AgentRun run, Agent agent, CancellationToken cancellationToken)
    {
        run.State = RunStates.Running;
        await db.SaveChangesAsync(cancellationToken);
        var links = await (from l in db.AgentTools.AsNoTracking() join t in db.Tools.AsNoTracking() on l.ToolId equals t.Id join s in db.Servers.AsNoTracking() on t.ServerId equals s.Id
                           where l.AgentId == agent.Id && l.Enabled orderby l.Sort, l.Id select new { Link = l, Tool = t, ServerKind = s.Kind }).ToListAsync(cancellationToken);
        if (!await db.Steps.AnyAsync(s => s.RunId == run.Id, cancellationToken))
        {
            await budget.EnsureAsync(run.StartedBy, cancellationToken);
            await AddStepAsync(run, "think", "Understand the task", run.Subject, "done", cancellationToken);
            var bases = await db.AgentBases.AsNoTracking().Where(b => b.AgentId == agent.Id).Select(b => b.BaseId).ToListAsync(cancellationToken);
            if (bases.Count > 0)
            {
                var clock = Stopwatch.StartNew();
                var found = await retriever.SearchAsync(run.Subject, bases, 4, cancellationToken);
                await AddStepAsync(run, "tool", "Search the knowledge bases", $"{found.Count} passages · {clock.ElapsedMilliseconds / 1000.0:0.0}s", "done", cancellationToken);
            }
        }
        while (run.Cursor < links.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = links[run.Cursor];
            var decided = await db.Approvals.AsNoTracking().Where(a => a.RunId == run.Id && a.ToolIndex == run.Cursor).OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (decided is { State: "pending" })
            {
                run.State = RunStates.WaitingApproval;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            if (decided is { State: "rejected" })
            {
                await AddStepAsync(run, "think", $"Not using {item.Tool.Name}", string.IsNullOrWhiteSpace(decided.Reason) ? "The approver rejected the call." : "The approver rejected the call: " + decided.Reason, "done", cancellationToken);
                run.Cursor++;
                continue;
            }
            if (decided is null && NeedsApproval(item.Tool.Risk, item.Link.Approval))
            {
                await RequestApprovalAsync(run, item.Tool, cancellationToken);
                return;
            }
            await EnsureLimitsAsync(run, agent, cancellationToken);
            var step = await AddStepAsync(run, "tool", $"Call {item.Tool.Name}", "", "running", cancellationToken);
            var clock2 = Stopwatch.StartNew();
            var outcome = await executor.ExecuteAsync(new ToolCall(item.Tool.Id, item.Tool.Name, item.Tool.Risk, item.ServerKind, run.Subject), cancellationToken);
            step.Detail = Cut($"{outcome.Summary} · {clock2.ElapsedMilliseconds / 1000.0:0.0}s", 2_000);
            step.State = "done";
            run.Cursor++;
            await db.SaveChangesAsync(cancellationToken);
            await PauseAsync(cancellationToken);
        }
        await EnsureLimitsAsync(run, agent, cancellationToken);
        await ReplyAsync(run, agent, cancellationToken);
        await FinishAsync(run, agent, RunStates.Succeeded, null, cancellationToken);
    }

    private async Task RequestApprovalAsync(AgentRun run, Tool tool, CancellationToken cancellationToken)
    {
        var step = await AddStepAsync(run, "approval", $"Waiting for approval: {tool.Name}", "A person has to confirm this call.", "waiting", cancellationToken);
        var approval = new AgentApproval
        {
            Id = Guid.NewGuid(), Source = "agent", SourceId = run.AgentId, RunId = run.Id, ToolIndex = run.Cursor, StepId = step.Id, Title = Cut($"{tool.Name}", 200), Detail = Cut(run.Subject, 1_000),
            ArgumentsJson = ApprovalArguments.ToJson([new("subject", Cut(run.Subject, 300)), new("risk", tool.Risk)]), Risk = tool.Risk, RequestedBy = run.StartedBy, CreatedAt = time.GetUtcNow(),
        };
        db.Approvals.Add(approval);
        run.State = RunStates.WaitingApproval;
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(AgentsEvents.RunWaitingApproval, Mapping.ToDto(approval), ContractsJsonContext.Default.ApprovalDto, cancellationToken);
    }

    private async Task ReplyAsync(AgentRun run, Agent agent, CancellationToken cancellationToken)
    {
        var steps = await db.Steps.AsNoTracking().Where(s => s.RunId == run.Id).OrderBy(s => s.Ordinal).ToListAsync(cancellationToken);
        var messages = new List<ModelMessage>();
        if (!string.IsNullOrWhiteSpace(agent.Instruction))
        {
            messages.Add(new ModelMessage("system", agent.Instruction));
        }
        messages.Add(new ModelMessage("user", $"{run.Subject}\n\nWhat was done so far:\n" + string.Join('\n', steps.Select(s => $"- {s.Title}: {s.Detail}"))));
        var model = await models.ResolveAsync(string.IsNullOrWhiteSpace(agent.ModelCode) ? null : agent.ModelCode, cancellationToken) ?? throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.NoModel, "No model is enabled.");
        var clock = Stopwatch.StartNew();
        var result = await gateway.CompleteAsync(models, model.Code, messages, cancellationToken);
        run.Tokens += result.TokensIn + result.TokensOut;
        run.Cost += result.Cost;
        await usage.RecordAsync(new UsageEntry(run.StartedBy, model.Id, model.ProviderId, model.DisplayName, "agent", run.Id, result.TokensIn, result.TokensOut, 0, 0, (int)clock.ElapsedMilliseconds, result.Cost,
            $"{{\"in\":{model.PriceIn},\"out\":{model.PriceOut},\"cache\":{model.PriceCache}}}"), cancellationToken);
        await AddStepAsync(run, "reply", "Write the reply", Cut(result.Text, 2_000), "done", cancellationToken);
        if (agent.MaxCost > 0 && run.Cost > agent.MaxCost)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.BudgetExceeded, "The run went over the agent's cost limit.");
        }
    }

    private async Task EnsureLimitsAsync(AgentRun run, Agent agent, CancellationToken cancellationToken)
    {
        if (await db.Steps.CountAsync(s => s.RunId == run.Id, cancellationToken) >= agent.MaxSteps)
        {
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.InvalidState, $"The run reached the agent's limit of {agent.MaxSteps} steps.");
        }
    }

    private async Task FinishAsync(AgentRun run, Agent agent, string state, string? error, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = time.GetUtcNow();
        var pending = await db.Steps.Where(s => s.RunId == run.Id && (s.State == "running" || s.State == "waiting")).ToListAsync(cancellationToken);
        foreach (var step in pending)
        {
            step.State = state == RunStates.Cancelled ? "cancelled" : "failed";
        }
        await db.Approvals.Where(a => a.RunId == run.Id && a.State == "pending").ExecuteUpdateAsync(s => s.SetProperty(a => a.State, "rejected").SetProperty(a => a.Reason, "The run ended.").SetProperty(a => a.DecidedAt, now), cancellationToken);
        var current = await db.Runs.SingleAsync(r => r.Id == run.Id, cancellationToken);
        current.State = state;
        current.EndedAt = now;
        current.Error = error is null ? null : Cut(error, 500);
        current.Tokens = run.Tokens;
        current.Cost = run.Cost;
        await db.SaveChangesAsync(cancellationToken);
        var succeeded = state == RunStates.Succeeded ? 1 : 0;
        await db.Agents.Where(a => a.Id == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.RunCount, a => a.RunCount + 1).SetProperty(a => a.SuccessCount, a => a.SuccessCount + succeeded), cancellationToken);
        await events.PublishAsync(AgentsEvents.RunCompleted, await Mapping.ToDtoAsync(db, agent.Name, current, cancellationToken), ContractsJsonContext.Default.RunDto, cancellationToken);
    }

    private async Task<RunStep> AddStepAsync(AgentRun run, string kind, string title, string detail, string state, CancellationToken cancellationToken)
    {
        var ordinal = await db.Steps.Where(s => s.RunId == run.Id).Select(s => (int?)s.Ordinal).MaxAsync(cancellationToken) ?? -1;
        var step = new RunStep { Id = Guid.NewGuid(), RunId = run.Id, Ordinal = ordinal + 1, Kind = kind, Title = Cut(title, 200), Detail = Cut(detail, 2_000), State = state, AtMs = (int)Math.Min((time.GetUtcNow() - run.StartedAt).TotalMilliseconds, int.MaxValue) };
        db.Steps.Add(step);
        await db.SaveChangesAsync(cancellationToken);
        await PauseAsync(cancellationToken);
        return step;
    }

    private Task PauseAsync(CancellationToken cancellationToken) => options.Value.StepDelayMs > 0 ? Task.Delay(options.Value.StepDelayMs, cancellationToken) : Task.CompletedTask;

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}

internal static class ApprovalArguments
{
    public static string ToJson(IReadOnlyList<KeyValuePair<string, string>> arguments) =>
        JsonSerializer.Serialize(arguments.Select(a => new[] { a.Key, a.Value }).ToArray(), ArgumentsJson.Default.StringArrayArray);

    public static IReadOnlyList<KeyValuePair<string, string>> FromJson(string json)
    {
        try
        {
            return [.. (JsonSerializer.Deserialize(json, ArgumentsJson.Default.StringArrayArray) ?? []).Where(p => p.Length == 2).Select(p => new KeyValuePair<string, string>(p[0], p[1]))];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string[][]))]
internal sealed partial class ArgumentsJson : System.Text.Json.Serialization.JsonSerializerContext;

internal static class Mapping
{
    public static ApprovalDto ToDto(AgentApproval a) =>
        new(a.Id, a.Source, a.RunId, a.Title, a.Detail, ApprovalArguments.FromJson(a.ArgumentsJson), a.Risk, a.State, a.Reason, a.CreatedAt, a.DecidedAt);

    public static RunStepDto ToDto(RunStep s) => new(s.Id, s.Ordinal, s.Kind, s.Title, s.Detail, s.State, s.AtMs);

    public static async Task<RunDto> ToDtoAsync(AgentsDbContext db, string agentName, AgentRun r, CancellationToken cancellationToken)
    {
        var steps = await db.Steps.AsNoTracking().Where(s => s.RunId == r.Id).OrderBy(s => s.Ordinal).ToListAsync(cancellationToken);
        return ToDto(agentName, r, steps);
    }

    public static RunDto ToDto(string agentName, AgentRun r, IEnumerable<RunStep> steps) =>
        new(r.Id, r.AgentId, agentName, r.Trigger, r.Subject, r.State, r.StartedAt, r.EndedAt, r.Tokens, r.Cost, [.. steps.OrderBy(s => s.Ordinal).Select(ToDto)], r.Version);
}

/// <summary>Runs, their steps and the approvals (design 4.3).</summary>
public sealed class RunService(AgentsDbContext db, AgentsService agents, IActorContext actors, RunLauncher launcher, RunRegistry registry,
    IEnumerable<IApprovalObserver> observers, IAuditLog audit, TimeProvider time)
{
    public async Task<RunDto> StartAsync(Guid agentId, StartRunRequest request, string trigger, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var agent = await agents.FindAsync(agentId, forWrite: false, tracking: false, cancellationToken);
        new Validator().Text("subject", request.Subject, 200).ThrowIfInvalid();
        if (agent.State != "published")
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "Publish the agent before running it.");
        }
        var run = new AgentRun { Id = Guid.NewGuid(), AgentId = agentId, StartedBy = actor.Id, Trigger = trigger, Subject = request.Subject.Trim(), State = RunStates.Queued, StartedAt = time.GetUtcNow() };
        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        launcher.Launch(run.Id);
        return Mapping.ToDto(agent.Name, run, []);
    }

    public async Task<Paged<RunDto>> ListAsync(Guid? agentId, string? state, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var id = actor.Id;
        var paging = PageRequest.Of(page, pageSize);
        var query = from r in db.Runs.AsNoTracking() join a in db.Agents.AsNoTracking() on r.AgentId equals a.Id where a.Visibility != "private" || a.OwnerId == id select new { Run = r, AgentName = a.Name };
        if (agentId is { } agent)
        {
            query = query.Where(x => x.Run.AgentId == agent);
        }
        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(x => x.Run.State == state);
        }
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.Run.StartedAt).ThenBy(x => x.Run.Id).Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellationToken);
        return paging.Wrap<RunDto>([.. rows.Select(x => Mapping.ToDto(x.AgentName, x.Run, []))], total);
    }

    public async Task<RunDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var (run, agent) = await FindAsync(id, cancellationToken);
        return await Mapping.ToDtoAsync(db, agent.Name, run, cancellationToken);
    }

    public async Task<RunDto> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var (run, agent) = await FindAsync(id, cancellationToken);
        if (run.StartedBy != actor.Id && agent.OwnerId != actor.Id && !actor.Can(Permissions.AgentsApprove))
        {
            throw ApiException.Forbidden("Only the person who started a run, the agent's owner or an approver can cancel it.");
        }
        if (RunStates.IsFinal(run.State))
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "The run has finished already.");
        }
        registry.Cancel(id);
        if (!registry.IsActive(id))
        {
            // Waiting for an approval: nothing is executing, so close it here.
            var now = time.GetUtcNow();
            await db.Approvals.Where(a => a.RunId == id && a.State == "pending").ExecuteUpdateAsync(s => s.SetProperty(a => a.State, "rejected").SetProperty(a => a.Reason, "The run was cancelled.").SetProperty(a => a.DecidedAt, now), cancellationToken);
            await db.Steps.Where(s => s.RunId == id && (s.State == "waiting" || s.State == "running")).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "cancelled"), cancellationToken);
            run.State = RunStates.Cancelled;
            run.EndedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            await db.Agents.Where(a => a.Id == agent.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.RunCount, a => a.RunCount + 1), cancellationToken);
        }
        else
        {
            await registry.WhenIdleAsync(id).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            db.ChangeTracker.Clear();
        }
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>The run's events as a person watches it: a <c>step</c> event for each new or changed step, a <c>run</c> event when its state changes, until it is final.</summary>
    public async IAsyncEnumerable<(string Event, string Json)> WatchAsync(Guid id, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await FindAsync(id, cancellationToken);
        var seen = new Dictionary<Guid, string>();
        string? lastState = null;
        while (true)
        {
            db.ChangeTracker.Clear();
            var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == id, cancellationToken);
            foreach (var step in await db.Steps.AsNoTracking().Where(s => s.RunId == id).OrderBy(s => s.Ordinal).ToListAsync(cancellationToken))
            {
                var key = step.State + "|" + step.Detail;
                if (!seen.TryGetValue(step.Id, out var known) || known != key)
                {
                    seen[step.Id] = key;
                    yield return ("step", JsonSerializer.Serialize(Mapping.ToDto(step), ContractsJsonContext.Default.RunStepDto));
                }
            }
            if (run.State != lastState)
            {
                lastState = run.State;
                var agent = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == run.AgentId, cancellationToken);
                yield return ("run", JsonSerializer.Serialize(Mapping.ToDto(agent.Name, run, []), ContractsJsonContext.Default.RunDto));
            }
            if (RunStates.IsFinal(run.State))
            {
                yield break;
            }
            await Task.Delay(300, cancellationToken);
        }
    }

    private async Task<(AgentRun Run, Agent Agent)> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var run = await db.Runs.SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw ApiException.NotFound("The run");
        var agent = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == run.AgentId, cancellationToken);
        if (agent.Visibility == "private" && agent.OwnerId != actor.Id)
        {
            throw ApiException.NotFound("The run");
        }
        return (run, agent);
    }

    // ---- approvals

    public async Task<IReadOnlyList<ApprovalDto>> ApprovalsAsync(string? state, CancellationToken cancellationToken)
    {
        var query = db.Approvals.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(a => a.State == state);
        }
        return [.. (await query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id).Take(200).ToListAsync(cancellationToken)).Select(Mapping.ToDto)];
    }

    public Task<ApprovalDto> ApproveAsync(Guid id, DecideApprovalRequest request, CancellationToken cancellationToken) => DecideAsync(id, true, request, cancellationToken);

    public Task<ApprovalDto> RejectAsync(Guid id, DecideApprovalRequest request, CancellationToken cancellationToken) => DecideAsync(id, false, request, cancellationToken);

    private async Task<ApprovalDto> DecideAsync(Guid id, bool approved, DecideApprovalRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        new Validator().Text("reason", request.Reason, 300, required: false).ThrowIfInvalid();
        var approval = await db.Approvals.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw ApiException.NotFound("The approval");
        if (approval.RequestedBy is { } requester && requester == actor.Id && actor.Type != ActorType.System && approval.Source == "agent" && actor.RoleId != Roles.Owner)
        {
            throw ApiException.Forbidden("Someone else has to approve what you started.");
        }
        var now = time.GetUtcNow();
        var state = approved ? "approved" : "rejected";
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        // One decision only: the first conditional update wins, a second one finds nothing pending.
        var claimed = await db.Approvals.Where(a => a.Id == id && a.State == "pending").ExecuteUpdateAsync(s => s.SetProperty(a => a.State, state).SetProperty(a => a.Reason, reason)
            .SetProperty(a => a.DecidedBy, actor.Id).SetProperty(a => a.DecidedAt, now), cancellationToken);
        if (claimed == 0)
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "The approval was decided already.");
        }
        await audit.RecordAsync(new AuditRecord(approved ? AgentsAuditActions.ApprovalApproved : AgentsAuditActions.ApprovalRejected, "approval", id.ToString(), approval.Title), cancellationToken);
        if (approval.StepId is { } stepId)
        {
            await db.Steps.Where(s => s.Id == stepId).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, approved ? "done" : "rejected").SetProperty(x => x.Detail, approved ? "Approved." : "Rejected" + (reason is null ? "." : ": " + reason)), cancellationToken);
        }
        db.ChangeTracker.Clear();
        approval = await db.Approvals.AsNoTracking().SingleAsync(a => a.Id == id, cancellationToken);
        if (approval is { Source: "agent", RunId: { } runId })
        {
            await db.Runs.Where(r => r.Id == runId && r.State == RunStates.WaitingApproval).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RunStates.Queued), cancellationToken);
            launcher.Launch(runId);
        }
        foreach (var observer in observers)
        {
            await observer.OnDecidedAsync(new ApprovalDecision(id, approval.Source, approval.SourceId, approved, reason), cancellationToken);
        }
        return Mapping.ToDto(approval);
    }
}

/// <summary>What chat and the workflows use to ask a person: the approval is kept here and shows in the approvals list.</summary>
internal sealed class ApprovalSink(AgentsDbContext db, IActorContext actors, TimeProvider time) : IApprovalSink
{
    public async Task<Guid?> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.GetAsync(cancellationToken);
        var approval = new AgentApproval
        {
            Id = Guid.NewGuid(), Source = request.Source.Length > 16 ? request.Source[..16] : request.Source, SourceId = request.SourceId, RunId = request.RunId,
            Title = request.Title.Length > 200 ? request.Title[..200] : request.Title, Detail = request.Detail.Length > 1_000 ? request.Detail[..1_000] : request.Detail,
            ArgumentsJson = ApprovalArguments.ToJson(request.Arguments), Risk = request.Risk.Length > 16 ? request.Risk[..16] : request.Risk, RequestedBy = actor?.Id, CreatedAt = time.GetUtcNow(),
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync(cancellationToken);
        return approval.Id;
    }
}
