using TinaClass.App.Common.Sample;
using TinaClass.App.Features.Agents;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Sample;
using TinaClass.Contracts.Workflows;

namespace TinaClass.App.Features.Flow;

/// <summary>
/// The sample workflow "工单自动分诊" (v12, Contracts/Workflows/WorkflowsSample) in memory. Run 842 waits at the confirmation before the email is sent: it is the
/// same story as the triage agent's approval, so the run ends when that approval has been decided (the sample asks <see cref="IAgentsService"/>). A run you start
/// moves a step every 1.2 seconds while <see cref="AutoAdvance"/> is on (tests and screenshots turn it off: the run is complete at once) and confirms the approval
/// node by itself; nothing is really executed.
/// </summary>
public sealed class SampleFlowService(IAgentsService agents) : IFlowService
{
    public static bool AutoAdvance { get; set; } = true;

    private const int StepMilliseconds = 1_200;
    private static readonly string[] Order = ["n1", "n2", "n3", "n4", "n5", "n6", "n7"];

    private FlowDto _flow = new(WorkflowsSample.FlowId, "工单自动分诊", 12, WorkflowsSample.Graph, false, SampleClock.At(2, 15, 20), 1);
    private readonly List<FlowRunDto> _runs = [];
    private readonly Dictionary<Guid, DateTimeOffset> _live = [];
    private int _ordinal = 842;

    private static readonly Guid Run842 = SampleIds.Run(8420);

    private List<FlowRunDto> Runs()
    {
        if (_runs.Count == 0)
        {
            var started = SampleClock.Now.AddSeconds(-50);
            _runs.Add(new FlowRunDto(Run842, _flow.Id, 12, RunStates.WaitingApproval, started, null, 31, 842, WorkflowsSample.Run842));
            for (var i = 0; i < 2; i++)
            {
                var at = SampleClock.At(i + 1, 10 + i, 12);
                _runs.Add(new FlowRunDto(Guid.NewGuid(), _flow.Id, 12, RunStates.Succeeded, at, at.AddSeconds(14), 28 + i, 841 - i, Finished(2 + i)));
            }
        }
        return _runs;
    }

    /// <summary>All the steps done, one after another (the last one skipped), each taking between 0.15 and 1 second.</summary>
    private static List<FlowNodeRunDto> Finished(int seed)
    {
        var at = 0;
        return
        [
            .. WorkflowsSample.Graph.Nodes.Select(n =>
            {
                var skipped = n.Key == "n8";
                var duration = skipped ? 0 : 150 + 220 * ((n.Key[1] + seed) % 5);
                var run = new FlowNodeRunDto(n.Key, skipped ? NodeStates.Skipped : NodeStates.Succeeded, skipped ? 0 : at, duration, skipped ? null : SampleClock.Pick("Done", "完成", "完成"));
                at += duration;
                return run;
            }),
        ];
    }

    public Task<FlowDto?> FlowAsync(CancellationToken cancellation = default) => Task.FromResult<FlowDto?>(_flow);

    public Task<IReadOnlyList<FlowRunDto>> RunsAsync(Guid flowId, CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<FlowRunDto>>([.. Runs().OrderByDescending(r => r.StartedAt).Take(5)]);

    public async Task<FlowRunDto> RunAsync(Guid runId, CancellationToken cancellation = default)
    {
        var run = Runs().First(r => r.Id == runId);
        if (run.Id == Run842 && run.State == RunStates.WaitingApproval && (await agents.PendingApprovalsAsync(cancellation)).Count == 0)
        {
            var nodes = WorkflowsSample.Run842.Select(n => n.Key switch
            {
                "n6" => n with { State = NodeStates.Succeeded, DurationMs = 5_200, Output = SampleClock.Pick("Approved", "已批准", "已批准") },
                "n7" => n with { State = NodeStates.Succeeded, StartMs = 14_000, DurationMs = 900, Output = SampleClock.Pick("Email sent", "邮件已发送", "郵件已發送") },
                _ => n,
            }).ToList();
            Replace(run = run with { State = RunStates.Succeeded, EndedAt = SampleClock.Now, Nodes = nodes });
        }
        else if (_live.TryGetValue(run.Id, out var started))
        {
            var count = Math.Min(Order.Length, 1 + (int)((DateTimeOffset.UtcNow - started).TotalMilliseconds / StepMilliseconds));
            var done = count >= Order.Length;
            var finished = Finished(1);
            var nodes = finished.Select(n => Array.IndexOf(Order, n.Key) is var at && (done || at < count - 1 || n.Key == "n8")
                ? n
                : at == count - 1 ? n with { State = NodeStates.Running, DurationMs = 0 } : n with { State = NodeStates.Pending, StartMs = 0, DurationMs = 0, Output = null }).ToList();
            Replace(run = run with { State = done ? RunStates.Succeeded : RunStates.Running, EndedAt = done ? SampleClock.Now : null, Nodes = nodes, Cost = 3 * count });
            if (done)
            {
                _live.Remove(run.Id);
            }
        }
        return run;
    }

    private void Replace(FlowRunDto run) => _runs[_runs.FindIndex(r => r.Id == run.Id)] = run;

    public Task<FlowDto> SaveDraftAsync(FlowDto flow, FlowGraphDto graph, CancellationToken cancellation = default)
    {
        _flow = _flow with { Draft = graph, HasChanges = true, UpdatedAt = SampleClock.Now, Version = _flow.Version + 1 };
        return Task.FromResult(_flow);
    }

    public Task<FlowDto> PublishAsync(Guid flowId, CancellationToken cancellation = default)
    {
        _flow = _flow with { PublishedVersion = _flow.PublishedVersion + 1, HasChanges = false, UpdatedAt = SampleClock.Now, Version = _flow.Version + 1 };
        return Task.FromResult(_flow);
    }

    public Task<FlowRunDto> StartRunAsync(Guid flowId, string? input, CancellationToken cancellation = default)
    {
        Runs();
        var run = new FlowRunDto(Guid.NewGuid(), flowId, _flow.PublishedVersion, AutoAdvance ? RunStates.Running : RunStates.Succeeded, SampleClock.Now, AutoAdvance ? null : SampleClock.Now.AddSeconds(8), AutoAdvance ? 0 : 18,
            ++_ordinal, AutoAdvance ? [.. Finished(1).Select(n => n.Key == "n1" ? n with { State = NodeStates.Running } : n with { State = NodeStates.Pending, StartMs = 0, DurationMs = 0, Output = null })] : Finished(1));
        _runs.Add(run);
        if (AutoAdvance)
        {
            _live[run.Id] = DateTimeOffset.UtcNow;
        }
        return Task.FromResult(run);
    }
}
