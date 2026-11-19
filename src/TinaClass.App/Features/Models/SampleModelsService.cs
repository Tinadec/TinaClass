using TinaClass.App.Common.Sample;
using TinaClass.Contracts.Models;
using TinaClass.Contracts.Sample;

namespace TinaClass.App.Features.Models;

/// <summary>The sample providers and models of 栖云科技 (Contracts/Sample), kept in memory so that switches and keys change what the app shows.</summary>
public sealed class SampleModelsService : IModelsService
{
    private readonly List<ProviderDto> _providers = [.. SampleCore.Providers.Select(SampleCore.ToDto)];
    private readonly List<ModelDto> _models = [.. SampleCore.Models.Select(SampleCore.ToDto)];

    public Task<IReadOnlyList<ProviderDto>> ProvidersAsync(CancellationToken cancellation = default) => Task.FromResult<IReadOnlyList<ProviderDto>>([.. _providers.OrderBy(p => p.Name == "TinaClass 内置示例").ThenBy(p => p.Name)]);

    public Task<IReadOnlyList<ModelDto>> ModelsAsync(bool all = false, CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<ModelDto>>([.. _models.Where(m => all || m.Enabled)]);

    public Task<ProviderTestResult> TestAsync(Guid providerId, CancellationToken cancellation = default)
    {
        var provider = _providers.Single(p => p.Id == providerId);
        var ok = !provider.RequiresKey || provider.KeySet;
        Replace(provider with { Status = ok ? "ok" : "no_key", LastLatencyMs = ok ? provider.LastLatencyMs ?? 240 : null, LastCheckAt = SampleClock.Now });
        return Task.FromResult(new ProviderTestResult(ok, ok ? provider.LastLatencyMs ?? 240 : 0, ok ? null : "key_required"));
    }

    public Task<ProviderDto> SetKeyAsync(Guid providerId, string key, CancellationToken cancellation = default)
    {
        var provider = _providers.Single(p => p.Id == providerId);
        var tail = key.Length >= 4 ? key[^4..] : key;
        return Task.FromResult(Replace(provider with { KeySet = true, KeyHint = "sk-••••••••" + tail, Status = "ok", LastLatencyMs = provider.LastLatencyMs ?? 240 }));
    }

    public Task<ProviderDto> ClearKeyAsync(Guid providerId, CancellationToken cancellation = default)
    {
        var provider = _providers.Single(p => p.Id == providerId);
        return Task.FromResult(Replace(provider with { KeySet = false, KeyHint = null, Status = provider.RequiresKey ? "no_key" : provider.Status }));
    }

    public Task<ProviderDto> SetProviderEnabledAsync(ProviderDto provider, bool enabled, CancellationToken cancellation = default) =>
        Task.FromResult(Replace(provider with { Enabled = enabled }));

    public Task<ModelDto> UpdateModelAsync(ModelDto model, bool? enabled, bool? isDefault, CancellationToken cancellation = default)
    {
        if (isDefault == true)
        {
            for (var i = 0; i < _models.Count; i++)
            {
                _models[i] = _models[i] with { IsDefault = _models[i].Id == model.Id };
            }
        }
        var index = _models.FindIndex(m => m.Id == model.Id);
        _models[index] = _models[index] with { Enabled = enabled ?? _models[index].Enabled };
        return Task.FromResult(_models[index]);
    }

    private ProviderDto Replace(ProviderDto updated)
    {
        _providers[_providers.FindIndex(p => p.Id == updated.Id)] = updated;
        return updated;
    }
}
