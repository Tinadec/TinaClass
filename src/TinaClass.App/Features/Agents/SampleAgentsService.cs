using TinaClass.App.Common.Sample;
using TinaClass.App.Features.Agents.Localization;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Sample;

namespace TinaClass.App.Features.Agents;

/// <summary>
/// The sample agents, tool servers and runs of 栖云科技 (Contracts/Agents/AgentsSample), in memory. Run 842 of the ticket triage agent waits for the approval to
/// send an email (<see cref="SampleIds.Approval"/> 1); approving it adds the step that sends it and ends the run. Starting a run adds the steps one at a time
/// (every 1.2 seconds) while <see cref="AutoAdvance"/> is on; tests and screenshots turn it off and the run is complete at once. Nothing is really executed.
/// </summary>
public sealed class SampleAgentsService : IAgentsService
{
    public static bool AutoAdvance { get; set; } = true;

    private const int StepMilliseconds = 1_200;

    private readonly List<RunDto> _runs = [];
    private readonly Dictionary<Guid, (DateTimeOffset Started, IReadOnlyList<RunStepDto> Script)> _live = [];
    private ApprovalDto? _approval;

    public event EventHandler? ApprovalsChanged;

    public SampleAgentsService()
    {
        var triage = AgentsSample.Agents[1];
        var started = SampleClock.Now.AddSeconds(-AgentsSample.Run842[^1].AtMs / 1000.0 - 40);
        _runs.Add(new RunDto(SampleIds.Run(842), triage.Id, triage.Name, "webhook", "工单 #48213：导出 12 万行卡在 80%", RunStates.WaitingApproval, started, null, 2_840, 31,
            [.. AgentsSample.Run842.Select((s, i) => new RunStepDto(Guid.NewGuid(), i, s.Kind, s.Title, s.Detail, s.State, s.AtMs))], 1));
        _approval = new ApprovalDto(SampleIds.Approval(1), "agent", SampleIds.Run(842), "发送邮件", "对外发送需要你确认", AgentsSample.ApprovalArguments, ToolRisks.External, "pending", null,
            started.AddSeconds(18), null);
        // Earlier runs: three for each agent, all fine, so every agent has a history.
        foreach (var (agent, a) in AgentsSample.Agents.Select((agent, a) => (agent, a)))
        {
            for (var i = 0; i < 3; i++)
            {
                var at = SampleClock.At(i + 1, 9 + a, 10 * i + 5);
                _runs.Add(new RunDto(Guid.NewGuid(), agent.Id, agent.Name, agent.Schedule is null ? "manual" : "schedule", Subjects[(a + i) % Subjects.Length], RunStates.Succeeded, at,
                    at.AddSeconds(24 + 7 * i), 1_100 + 230 * i + 90 * a, 6 + 3 * i, Script(i + 2), 1));
            }
        }
    }

    private static readonly string[] Subjects = ["本周新增的 12 条工单", "月度续费客户名单", "导出相关的反馈汇总", "新版本发布后的差评", "客户 #1042 的套餐咨询", "会议录音 11-12"];

    /// <summary>The steps of a short run: think, search, reply (the first <paramref name="count"/> of them), each with its time.</summary>
    private static List<RunStepDto> Script(int count)
    {
        var all = new (string Kind, string Title, string Detail, int At)[]
        {
            ("think", Text(AgentsLangResourceKind.SampleStartThink), SampleClock.Pick("Read the task and plan the steps", "读懂任务，规划步骤", "讀懂任務，規劃步驟"), 0),
            ("tool", Text(AgentsLangResourceKind.SampleStartTool), SampleClock.Pick("4 passages · 0.7s", "4 个片段 · 0.7s", "4 個片段 · 0.7s"), 1_500),
            ("reply", Text(AgentsLangResourceKind.SampleStartReply), SampleClock.Pick("Wrote the result", "已写出结果", "已寫出結果"), 4_000),
        };
        return [.. all.Take(Math.Clamp(count, 1, 3)).Select((s, i) => new RunStepDto(Guid.NewGuid(), i, s.Kind, s.Title, s.Detail, "done", s.At))];
    }

    private static string Text(AgentsLangResourceKind kind) => AgentsLangResources.Get(kind);

    public Task<IReadOnlyList<AgentDto>> AgentsAsync(CancellationToken cancellation = default)
    {
        IReadOnlyList<AgentDto> agents =
        [
            .. AgentsSample.Agents.Select(a => new AgentDto(a.Id, a.Name, a.Description, a.Instruction, a.ModelCode, a.MaxSteps, a.MaxCost, "workspace", "published", a.Schedule, a.Icon,
                [.. a.ToolIds.Select((id, n) => AgentsSample.Tools.First(t => t.Id == id) is var t ? new AgentToolRef(id, t.Name, t.Risk, true, a.Approvals[n]) : null!)],
                a.Id == AgentsSample.Agents[1].Id ? [SampleIds.Base(3)] : [], a.Runs, a.SuccessPercent, _runs.FirstOrDefault(r => r.AgentId == a.Id && !RunStates.IsFinal(r.State))?.State, 1)),
        ];
        return Task.FromResult(agents);
    }

    public Task<IReadOnlyList<ToolServerDto>> ServersAsync(CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<ToolServerDto>>([.. AgentsSample.Servers.Select(s => new ToolServerDto(s.Id, s.Name, s.Kind, s.Endpoint, s.Status, AgentsSample.Tools.Count(t => t.ServerId == s.Id),
            s.Status == "ok" ? SampleClock.Now.AddMinutes(-3) : SampleClock.Now.AddHours(-5)))]);

    public Task<IReadOnlyList<RunDto>> RunsAsync(Guid agentId, CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<RunDto>>([.. _runs.Where(r => r.AgentId == agentId).OrderByDescending(r => r.StartedAt).Take(5).Select(Advance)]);

    public Task<RunDto> RunAsync(Guid runId, CancellationToken cancellation = default) => Task.FromResult(Advance(_runs.First(r => r.Id == runId)));

    public Task<RunDto> StartRunAsync(Guid agentId, string subject, CancellationToken cancellation = default)
    {
        var agent = AgentsSample.Agents.First(a => a.Id == agentId);
        var script = Script(3);
        var started = SampleClock.Now;
        var run = new RunDto(Guid.NewGuid(), agentId, agent.Name, "manual", subject, AutoAdvance ? RunStates.Running : RunStates.Succeeded, started, AutoAdvance ? null : started.AddSeconds(5), 0, 0,
            AutoAdvance ? [script[0]] : script, 1);
        _runs.Add(run);
        if (AutoAdvance)
        {
            _live[run.Id] = (DateTimeOffset.UtcNow, script);
        }
        return Task.FromResult(run);
    }

    /// <summary>Moves a started run along by the time that has passed (a step every 1.2 seconds), and ends it after the last.</summary>
    private RunDto Advance(RunDto run)
    {
        if (!_live.TryGetValue(run.Id, out var live))
        {
            return run;
        }
        var count = 1 + (int)((DateTimeOffset.UtcNow - live.Started).TotalMilliseconds / StepMilliseconds);
        var done = count >= live.Script.Count;
        var next = run with
        {
            Steps = [.. live.Script.Take(Math.Min(count, live.Script.Count))],
            State = done ? RunStates.Succeeded : RunStates.Running,
            EndedAt = done ? run.StartedAt.AddSeconds(5) : null,
            Tokens = 400 * Math.Min(count, live.Script.Count),
            Cost = 2 * Math.Min(count, live.Script.Count),
        };
        Replace(next);
        if (done)
        {
            _live.Remove(run.Id);
        }
        return next;
    }

    private void Replace(RunDto run) => _runs[_runs.FindIndex(r => r.Id == run.Id)] = run;

    public Task<IReadOnlyList<ApprovalDto>> PendingApprovalsAsync(CancellationToken cancellation = default) => Task.FromResult<IReadOnlyList<ApprovalDto>>(_approval is null ? [] : [_approval]);

    public Task DecideAsync(Guid approvalId, bool approve, string? reason, CancellationToken cancellation = default)
    {
        if (_approval is { } approval && approval.Id == approvalId && approval.RunId is { } runId)
        {
            var run = _runs.First(r => r.Id == runId);
            var waiting = run.Steps.Last();
            var steps = run.Steps.Take(run.Steps.Count - 1).ToList();
            if (approve)
            {
                steps.Add(waiting with { State = "done", Title = waiting.Title.Replace("等待批准：", string.Empty) });
                steps.Add(new RunStepDto(Guid.NewGuid(), steps.Count, "tool", Text(AgentsLangResourceKind.SampleSent), Text(AgentsLangResourceKind.SampleSentDetail), "done", waiting.AtMs + 4_000));
            }
            else
            {
                steps.Add(waiting with { State = "failed", Detail = AgentsLangResources.Format(AgentsLangResourceKind.SampleStopped, reason ?? string.Empty) });
            }
            Replace(run with { Steps = steps, State = approve ? RunStates.Succeeded : RunStates.Failed, EndedAt = SampleClock.Now, Cost = run.Cost + (approve ? 1 : 0) });
            _approval = null;
            ApprovalsChanged?.Invoke(this, EventArgs.Empty);
        }
        return Task.CompletedTask;
    }
}
