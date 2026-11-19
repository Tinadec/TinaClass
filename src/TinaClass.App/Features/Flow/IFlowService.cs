using TinaClass.Contracts.Workflows;

namespace TinaClass.App.Features.Flow;

/// <summary>
/// The workflow of the workspace: its draft graph, the published version, and its runs. The sample keeps them in memory; <see cref="Api.ApiFlowService"/>
/// asks the API (/flows, /flow-runs). A run is read again while it goes; the approval node's decision is made through the agents' approvals.
/// </summary>
public interface IFlowService
{
    /// <summary>The first workflow, or null when there is none.</summary>
    Task<FlowDto?> FlowAsync(CancellationToken cancellation = default);

    /// <summary>The newest runs, newest first.</summary>
    Task<IReadOnlyList<FlowRunDto>> RunsAsync(Guid flowId, CancellationToken cancellation = default);

    Task<FlowRunDto> RunAsync(Guid runId, CancellationToken cancellation = default);

    Task<FlowDto> SaveDraftAsync(FlowDto flow, FlowGraphDto graph, CancellationToken cancellation = default);

    Task<FlowDto> PublishAsync(Guid flowId, CancellationToken cancellation = default);

    Task<FlowRunDto> StartRunAsync(Guid flowId, string? input, CancellationToken cancellation = default);
}
