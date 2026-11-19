using TinaClass.App.Common.Api;
using TinaClass.Contracts;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Common;

namespace TinaClass.App.Features.Agents.Api;

/// <summary>The agents of the API. Runs and approvals live on the server; the page polls the run it shows.</summary>
public sealed class ApiAgentsService(ApiClient api) : IAgentsService
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public event EventHandler? ApprovalsChanged;

    public Task<IReadOnlyList<AgentDto>> AgentsAsync(CancellationToken cancellation = default) => api.GetAsync(ApiRoutes.Agents, Json.IReadOnlyListAgentDto, cancellation);

    public Task<IReadOnlyList<ToolServerDto>> ServersAsync(CancellationToken cancellation = default) => api.GetAsync(ApiRoutes.ToolServers, Json.IReadOnlyListToolServerDto, cancellation);

    public async Task<IReadOnlyList<RunDto>> RunsAsync(Guid agentId, CancellationToken cancellation = default) =>
        (await api.GetAsync($"{ApiRoutes.Runs}?agentId={agentId}&pageSize=5", Json.PagedRunDto, cancellation)).Items;

    public Task<RunDto> RunAsync(Guid runId, CancellationToken cancellation = default) => api.GetAsync($"{ApiRoutes.Runs}/{runId}", Json.RunDto, cancellation);

    public Task<RunDto> StartRunAsync(Guid agentId, string subject, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Agents}/{agentId}/runs", new StartRunRequest(subject), Json.StartRunRequest, Json.RunDto, cancellation);

    public Task<IReadOnlyList<ApprovalDto>> PendingApprovalsAsync(CancellationToken cancellation = default) =>
        api.GetAsync($"{ApiRoutes.Approvals}?state=pending", Json.IReadOnlyListApprovalDto, cancellation);

    public async Task DecideAsync(Guid approvalId, bool approve, string? reason, CancellationToken cancellation = default)
    {
        await api.PostAsync($"{ApiRoutes.Approvals}/{approvalId}/{(approve ? "approve" : "reject")}", new DecideApprovalRequest(reason), Json.DecideApprovalRequest, Json.ApprovalDto, cancellation);
        ApprovalsChanged?.Invoke(this, EventArgs.Empty);
    }
}
