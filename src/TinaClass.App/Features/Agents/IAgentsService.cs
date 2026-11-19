using TinaClass.Contracts.Agents;

namespace TinaClass.App.Features.Agents;

/// <summary>
/// Agents, their runs and the approvals that runs wait for. The sample keeps them in memory; <see cref="Api.ApiAgentsService"/> asks the API (/agents,
/// /runs, /approvals, /tool-servers). A decided approval raises <see cref="ApprovalsChanged"/> (the sidebar's count and the pages listen).
/// </summary>
public interface IAgentsService
{
    event EventHandler? ApprovalsChanged;

    Task<IReadOnlyList<AgentDto>> AgentsAsync(CancellationToken cancellation = default);

    Task<IReadOnlyList<ToolServerDto>> ServersAsync(CancellationToken cancellation = default);

    /// <summary>The newest runs of an agent, newest first, with their steps.</summary>
    Task<IReadOnlyList<RunDto>> RunsAsync(Guid agentId, CancellationToken cancellation = default);

    Task<RunDto> RunAsync(Guid runId, CancellationToken cancellation = default);

    Task<RunDto> StartRunAsync(Guid agentId, string subject, CancellationToken cancellation = default);

    Task<IReadOnlyList<ApprovalDto>> PendingApprovalsAsync(CancellationToken cancellation = default);

    Task DecideAsync(Guid approvalId, bool approve, string? reason, CancellationToken cancellation = default);
}
