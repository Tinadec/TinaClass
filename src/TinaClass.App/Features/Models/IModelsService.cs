using TinaClass.Contracts.Models;

namespace TinaClass.App.Features.Models;

/// <summary>
/// Where the app gets its providers and models. The sample service keeps them in memory; <c>Api/ApiModelsService.cs</c> talks to the
/// API. Keys go in through <see cref="SetKeyAsync"/> and never come back: a provider only says whether one is set and its last four characters.
/// </summary>
public interface IModelsService
{
    /// <summary>The providers (empty for someone who may not manage them).</summary>
    Task<IReadOnlyList<ProviderDto>> ProvidersAsync(CancellationToken cancellation = default);

    /// <summary>The models a person can pick; with <paramref name="all"/> the disabled ones too (for the models page).</summary>
    Task<IReadOnlyList<ModelDto>> ModelsAsync(bool all = false, CancellationToken cancellation = default);

    Task<ProviderTestResult> TestAsync(Guid providerId, CancellationToken cancellation = default);

    Task<ProviderDto> SetKeyAsync(Guid providerId, string key, CancellationToken cancellation = default);

    Task<ProviderDto> ClearKeyAsync(Guid providerId, CancellationToken cancellation = default);

    Task<ProviderDto> SetProviderEnabledAsync(ProviderDto provider, bool enabled, CancellationToken cancellation = default);

    Task<ModelDto> UpdateModelAsync(ModelDto model, bool? enabled, bool? isDefault, CancellationToken cancellation = default);
}
