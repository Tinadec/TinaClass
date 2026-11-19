using TinaClass.App.Common.Api;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Models;

namespace TinaClass.App.Features.Models.Api;

/// <summary>The models and providers of the API (<c>/providers</c>, <c>/models</c>).</summary>
public sealed class ApiModelsService(ApiClient api) : IModelsService
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public async Task<IReadOnlyList<ProviderDto>> ProvidersAsync(CancellationToken cancellation = default)
    {
        // Only people who manage models see the providers; for everyone else the list is empty.
        if (!api.Session.Can(Permissions.ModelsManage))
        {
            return [];
        }
        return await api.GetAsync(ApiRoutes.Providers, Json.IReadOnlyListProviderDto, cancellation);
    }

    public Task<IReadOnlyList<ModelDto>> ModelsAsync(bool all = false, CancellationToken cancellation = default) =>
        api.GetAsync(ApiRoutes.Models + (all ? "?all=true" : string.Empty), Json.IReadOnlyListModelDto, cancellation);

    public Task<ProviderTestResult> TestAsync(Guid providerId, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Providers}/{providerId}/test", Json.ProviderTestResult, cancellation);

    public Task<ProviderDto> SetKeyAsync(Guid providerId, string key, CancellationToken cancellation = default) =>
        api.PutAsync($"{ApiRoutes.Providers}/{providerId}/key", new SetKeyRequest(key), Json.SetKeyRequest, Json.ProviderDto, cancellation);

    public Task<ProviderDto> ClearKeyAsync(Guid providerId, CancellationToken cancellation = default) =>
        api.DeleteAsync($"{ApiRoutes.Providers}/{providerId}/key", Json.ProviderDto, cancellation);

    public Task<ProviderDto> SetProviderEnabledAsync(ProviderDto provider, bool enabled, CancellationToken cancellation = default) =>
        api.PatchAsync($"{ApiRoutes.Providers}/{provider.Id}", new UpdateProviderRequest(null, null, enabled, provider.Version), Json.UpdateProviderRequest, Json.ProviderDto, cancellation);

    public Task<ModelDto> UpdateModelAsync(ModelDto model, bool? enabled, bool? isDefault, CancellationToken cancellation = default) =>
        api.PatchAsync($"{ApiRoutes.Models}/{model.Id}", new UpdateModelRequest(null, enabled, isDefault, model.Version), Json.UpdateModelRequest, Json.ModelDto, cancellation);
}
