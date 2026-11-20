using TinaClass.App.Common.Sample;
using TinaClass.App.Common.Settings;
using TinaClass.Contracts.Models;
using TinaClass.Contracts.Sample;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinaClass.App.Features.Models;

/// <summary>
/// The sample providers and models of 栖云科技 (Contracts/Sample). Switches, keys and the default model are saved to the
/// sample store (<c>sample-store.json</c>) so they survive a restart; the API mode never reads it.
/// </summary>
public sealed class SampleModelsService : IModelsService
{
    private const string Feature = "models";

    private readonly List<ProviderDto> _providers;
    private readonly List<ModelDto> _models;

    public SampleModelsService()
    {
        var saved = SampleStore.Read(Feature);
        _providers = [.. SampleCore.Providers.Select(SampleCore.ToDto)];
        _models = [.. SampleCore.Models.Select(SampleCore.ToDto)];
        if (saved.TryGetValue("data", out var data))
        {
            var s = data.Deserialize(SampleModelsJsonContext.Default.Saved);
            if (s is not null)
            {
                foreach (var ps in s.Providers)
                {
                    var i = _providers.FindIndex(p => p.Id == ps.Id);
                    if (i >= 0)
                    {
                        _providers[i] = _providers[i] with { Enabled = ps.Enabled, KeySet = ps.KeySet, KeyHint = ps.KeyHint, Status = ps.Status ?? _providers[i].Status };
                    }
                }
                foreach (var ms in s.Models)
                {
                    var i = _models.FindIndex(m => m.Id == ms.Id);
                    if (i >= 0)
                    {
                        _models[i] = _models[i] with { Enabled = ms.Enabled, IsDefault = ms.IsDefault };
                    }
                }
            }
        }
    }

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
        Save();
        return Task.FromResult(_models[index]);
    }

    private ProviderDto Replace(ProviderDto updated)
    {
        _providers[_providers.FindIndex(p => p.Id == updated.Id)] = updated;
        Save();
        return updated;
    }

    private void Save()
    {
        var saved = new Saved
        {
            Providers = [.. _providers.Select(p => new SavedProvider(p.Id, p.Enabled, p.KeySet, p.KeyHint, p.Status))],
            Models = [.. _models.Select(m => new SavedModel(m.Id, m.Enabled, m.IsDefault))],
        };
        SampleStore.Write(Feature, new Dictionary<string, JsonElement>
        {
            ["data"] = JsonSerializer.SerializeToElement(saved, SampleModelsJsonContext.Default.Saved),
        });
    }

    internal sealed record SavedProvider(Guid Id, bool Enabled, bool KeySet, string? KeyHint, string? Status);

    internal sealed record SavedModel(Guid Id, bool Enabled, bool IsDefault);

    internal sealed class Saved
    {
        public List<SavedProvider> Providers { get; set; } = [];
        public List<SavedModel> Models { get; set; } = [];
    }
}

[JsonSerializable(typeof(SampleModelsService.Saved))]
internal sealed partial class SampleModelsJsonContext : JsonSerializerContext;
