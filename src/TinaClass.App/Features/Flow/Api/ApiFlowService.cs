using TinaClass.App.Common.Api;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Workflows;

namespace TinaClass.App.Features.Flow.Api;

/// <summary>The workflows of the API (<c>/flows</c>, <c>/flow-runs</c>): the draft is saved with its version (a stale one is a 409 the page shows), a run goes on at the server.</summary>
public sealed class ApiFlowService(ApiClient api) : IFlowService
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public async Task<FlowDto?> FlowAsync(CancellationToken cancellation = default) => (await api.GetAsync(ApiRoutes.Flows, Json.IReadOnlyListFlowDto, cancellation)).FirstOrDefault();

    public Task<IReadOnlyList<FlowRunDto>> RunsAsync(Guid flowId, CancellationToken cancellation = default) => api.GetAsync($"{ApiRoutes.Flows}/{flowId}/runs", Json.IReadOnlyListFlowRunDto, cancellation);

    public Task<FlowRunDto> RunAsync(Guid runId, CancellationToken cancellation = default) => api.GetAsync($"{ApiRoutes.FlowRuns}/{runId}", Json.FlowRunDto, cancellation);

    public Task<FlowDto> SaveDraftAsync(FlowDto flow, FlowGraphDto graph, CancellationToken cancellation = default) =>
        api.PutAsync($"{ApiRoutes.Flows}/{flow.Id}/draft", new PutDraftRequest(graph, flow.Version), Json.PutDraftRequest, Json.FlowDto, cancellation);

    public Task<FlowDto> PublishAsync(Guid flowId, CancellationToken cancellation = default) => api.PostAsync($"{ApiRoutes.Flows}/{flowId}/publish", Json.FlowDto, cancellation);

    public Task<FlowRunDto> StartRunAsync(Guid flowId, string? input, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Flows}/{flowId}/runs", new StartFlowRunRequest(input), Json.StartFlowRunRequest, Json.FlowRunDto, cancellation);
}
