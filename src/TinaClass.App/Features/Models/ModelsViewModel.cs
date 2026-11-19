using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Models.Localization;
using TinaClass.Contracts.Models;
using TinaClass.Contracts.Common;
using TinaClass.App.Common.Ui;

namespace TinaClass.App.Features.Models;

/// <summary>One provider as the page shows it: what it is, how it is doing, and its on/off switch.</summary>
public sealed partial class ProviderRow : ObservableObject
{
    private readonly Func<ProviderRow, bool, Task> _setEnabled;
    private bool _syncing;

    public ProviderRow(ProviderDto dto, Func<ProviderRow, bool, Task> setEnabled)
    {
        Dto = dto;
        _setEnabled = setEnabled;
        _isEnabled = dto.Enabled;
    }

    public ProviderDto Dto { get; private set; }

    public Guid Id => Dto.Id;

    public string Name => Dto.Name;

    public string Monogram => Dto.Name.Length > 0 ? Dto.Name[..1].ToUpperInvariant() : "?";

    public string Detail => Dto.Protocol == ProviderProtocols.Echo ? ModelsLangResources.Get(ModelsLangResourceKind.Offline) : Dto.BaseUrl;

    public string StatusText => !Dto.Enabled ? ModelsLangResources.Get(ModelsLangResourceKind.StatusDisabled) : Dto.Status switch
    {
        "ok" => ModelsLangResources.Get(ModelsLangResourceKind.StatusOk) + (Dto.LastLatencyMs is { } ms ? " · " + string.Format(ModelsLangResources.Get(ModelsLangResourceKind.Latency), ms) : string.Empty),
        "no_key" => ModelsLangResources.Get(ModelsLangResourceKind.StatusNoKey),
        _ => ModelsLangResources.Get(ModelsLangResourceKind.StatusError),
    };

    public StatusTone Tone => !Dto.Enabled ? StatusTone.Muted : Dto.Status switch
    {
        "ok" => StatusTone.Success,
        "no_key" => StatusTone.Warning,
        _ => StatusTone.Danger,
    };

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isTesting;

    public void Update(ProviderDto dto)
    {
        Dto = dto;
        _syncing = true;
        IsEnabled = dto.Enabled;
        _syncing = false;
        OnPropertyChanged(string.Empty);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_syncing && value != Dto.Enabled)
        {
            _ = _setEnabled(this, value);
        }
    }

    /// <summary>The text follows the language.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>One model as the table shows it.</summary>
public sealed partial class ModelRow : ObservableObject
{
    private readonly Func<ModelRow, bool?, bool?, Task> _update;
    private bool _syncing;

    public ModelRow(ModelDto dto, Func<ModelRow, bool?, bool?, Task> update)
    {
        Dto = dto;
        _update = update;
        _isEnabled = dto.Enabled;
    }

    public ModelDto Dto { get; private set; }

    public string Name => Dto.DisplayName;

    public string Code => Dto.Code;

    public string Provider => Dto.ProviderName;

    public string Context => Dto.ContextTokens >= 1_000 ? (Dto.ContextTokens / 1_000) + "K" : Dto.ContextTokens.ToString();

    public bool IsDefault => Dto.IsDefault;

    public string Price => Dto.Kind == "embedding" ? Money.Format(Dto.PriceIn, 0) + " · " + ModelsLangResources.Get(ModelsLangResourceKind.Embedding)
        : Dto.PriceIn == 0 && Dto.PriceOut == 0 ? ModelsLangResources.Get(ModelsLangResourceKind.Free)
        : Money.Format(Dto.PriceIn, 0) + " / " + Money.Format(Dto.PriceOut, 0);

    [ObservableProperty]
    private bool _isEnabled;

    public void Update(ModelDto dto)
    {
        Dto = dto;
        _syncing = true;
        IsEnabled = dto.Enabled;
        _syncing = false;
        OnPropertyChanged(string.Empty);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_syncing && value != Dto.Enabled)
        {
            _ = _update(this, value, null);
        }
    }

    public Task MakeDefaultAsync() => _update(this, null, true);

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>The models page: providers (with a connection test), their keys, and the model table.</summary>
public sealed partial class ModelsViewModel(IModelsService service) : PageViewModel
{
    public ObservableCollection<ProviderRow> Providers { get; } = [];

    public ObservableCollection<ModelRow> Models { get; } = [];

    public IModelsService Service => service;

    protected override async Task LoadAsync()
    {
        try
        {
            var providers = await service.ProvidersAsync();
            var models = await service.ModelsAsync(all: true);
            Providers.Clear();
            foreach (var p in providers)
            {
                Providers.Add(new ProviderRow(p, SetProviderEnabledAsync));
            }
            Models.Clear();
            foreach (var m in models)
            {
                Models.Add(new ModelRow(m, UpdateModelAsync));
            }
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    public async Task TestAsync(ProviderRow row)
    {
        row.IsTesting = true;
        try
        {
            await service.TestAsync(row.Id);
            await ReloadProvidersAsync();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
        finally
        {
            row.IsTesting = false;
        }
    }

    public async Task ReloadProvidersAsync()
    {
        var latest = await service.ProvidersAsync();
        foreach (var dto in latest)
        {
            Providers.FirstOrDefault(p => p.Id == dto.Id)?.Update(dto);
        }
    }

    public async Task SetKeyAsync(Guid providerId, string key)
    {
        await service.SetKeyAsync(providerId, key);
        await ReloadProvidersAsync();
    }

    public async Task ClearKeyAsync(Guid providerId)
    {
        await service.ClearKeyAsync(providerId);
        await ReloadProvidersAsync();
    }

    private async Task SetProviderEnabledAsync(ProviderRow row, bool enabled)
    {
        try
        {
            row.Update(await service.SetProviderEnabledAsync(row.Dto, enabled));
        }
        catch (ServiceException e)
        {
            Error = e.Message;
            row.Update(row.Dto);
        }
    }

    private async Task UpdateModelAsync(ModelRow row, bool? enabled, bool? isDefault)
    {
        try
        {
            var updated = await service.UpdateModelAsync(row.Dto, enabled, isDefault);
            if (isDefault == true)
            {
                foreach (var other in Models.ToList())
                {
                    other.Update(other.Dto with { IsDefault = other.Dto.Id == updated.Id });
                }
            }
            row.Update(updated with { IsDefault = isDefault == true || updated.IsDefault });
        }
        catch (ServiceException e)
        {
            Error = e.Message;
            row.Update(row.Dto);
        }
    }

    /// <summary>The text follows the language.</summary>
    public void RefreshTexts()
    {
        foreach (var p in Providers)
        {
            p.Refresh();
        }
        foreach (var m in Models)
        {
            m.Refresh();
        }
    }
}
