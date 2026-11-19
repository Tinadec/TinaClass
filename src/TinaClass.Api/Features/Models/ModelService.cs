using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Features.Models.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Models;

namespace TinaClass.Api.Features.Models;

/// <summary>Providers, their keys and their models (design 5 and 7). Keys go in and never come out.</summary>
public sealed class ModelService(ModelsDbContext db, KeyVault vault, ProviderProbe probe, IAuditLog audit, IEventPublisher events, TimeProvider time)
{
    public static bool RequiresKey(string protocol) => protocol is not (ProviderProtocols.Echo or ProviderProtocols.Local);

    public async Task<IReadOnlyList<ProviderDto>> ListProvidersAsync(CancellationToken cancellationToken)
    {
        var providers = await db.Providers.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        var counts = await db.ModelEntries.AsNoTracking().GroupBy(m => m.ProviderId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return [.. providers.Select(p => ToDto(p, counts.GetValueOrDefault(p.Id)))];
    }

    public async Task<ProviderDto> CreateProviderAsync(CreateProviderRequest request, CancellationToken cancellationToken)
    {
        var validator = new Validator().Text("name", request.Name, 80).Text("baseUrl", request.BaseUrl, 300);
        if (request.Protocol is not (ProviderProtocols.OpenAiCompatible or ProviderProtocols.Messages or ProviderProtocols.Local))
        {
            validator.Add("protocol", "Use openai-compatible, messages or local (the built-in Echo provider already exists).");
        }
        ValidateUrl(validator, request.BaseUrl);
        validator.ThrowIfInvalid();
        var order = await db.Providers.Select(p => (int?)p.SortOrder).MaxAsync(cancellationToken) ?? 0;
        var provider = new AiProvider { Id = Guid.NewGuid(), Name = request.Name.Trim(), Protocol = request.Protocol, BaseUrl = request.BaseUrl.Trim(), SortOrder = order + 1 };
        db.Providers.Add(provider);
        await db.SaveChangesAsync(cancellationToken);
        var dto = ToDto(provider, 0);
        await audit.RecordAsync(new AuditRecord(ModelsEvents.ProviderCreated, "provider", provider.Id.ToString(), provider.Name), cancellationToken);
        await events.PublishAsync(ModelsEvents.ProviderCreated, dto, ContractsJsonContext.Default.ProviderDto, cancellationToken);
        return dto;
    }

    public async Task<ProviderDto> UpdateProviderAsync(Guid id, UpdateProviderRequest request, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        var validator = new Validator();
        if (request.Name is not null)
        {
            validator.Text("name", request.Name, 80);
        }
        if (request.BaseUrl is not null)
        {
            validator.Text("baseUrl", request.BaseUrl, 300);
            ValidateUrl(validator, request.BaseUrl);
        }
        validator.ThrowIfInvalid();
        if (provider.Protocol == ProviderProtocols.Echo && (request.BaseUrl is not null || request.Enabled == false))
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "The built-in Echo provider cannot be changed or switched off.");
        }
        db.ExpectVersion(provider, request.Version);
        provider.Name = request.Name?.Trim() ?? provider.Name;
        provider.BaseUrl = request.BaseUrl?.Trim() ?? provider.BaseUrl;
        provider.Enabled = request.Enabled ?? provider.Enabled;
        if (request.Enabled == false)
        {
            // A provider that is off cannot hold the default model.
            await db.ModelEntries.Where(m => m.ProviderId == id && m.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDefault, false), cancellationToken);
            await EnsureDefaultAsync(except: id, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        var dto = ToDto(provider, await db.ModelEntries.CountAsync(m => m.ProviderId == id, cancellationToken));
        await audit.RecordAsync(new AuditRecord(ModelsEvents.ProviderUpdated, "provider", id.ToString(), provider.Name), cancellationToken);
        await events.PublishAsync(ModelsEvents.ProviderUpdated, dto, ContractsJsonContext.Default.ProviderDto, cancellationToken);
        return dto;
    }

    public async Task DeleteProviderAsync(Guid id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider.Protocol == ProviderProtocols.Echo)
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "The built-in Echo provider cannot be deleted.");
        }
        var hadDefault = await db.ModelEntries.AnyAsync(m => m.ProviderId == id && m.IsDefault, cancellationToken);
        db.Providers.Remove(provider);
        await db.SaveChangesAsync(cancellationToken);
        if (hadDefault)
        {
            await EnsureDefaultAsync(except: null, cancellationToken);
        }
        await audit.RecordAsync(new AuditRecord(ModelsEvents.ProviderDeleted, "provider", id.ToString(), provider.Name), cancellationToken);
    }

    public async Task<ProviderDto> SetKeyAsync(Guid id, SetKeyRequest request, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        var key = request.Key?.Trim();
        var validator = new Validator().Text("key", key, 400);
        if (!RequiresKey(provider.Protocol))
        {
            validator.Add("key", "This provider needs no key.");
        }
        validator.ThrowIfInvalid();
        provider.KeyCipher = vault.Protect(key!);
        provider.KeyHint = KeyVault.Hint(key!);
        provider.Status = "unchecked";
        await db.SaveChangesAsync(cancellationToken);
        // The audit entry names the provider, never the key.
        await audit.RecordAsync(new AuditRecord(ModelsEvents.KeyChanged, "provider", id.ToString(), provider.Name), cancellationToken);
        return ToDto(provider, await db.ModelEntries.CountAsync(m => m.ProviderId == id, cancellationToken));
    }

    public async Task<ProviderDto> ClearKeyAsync(Guid id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        provider.KeyCipher = null;
        provider.KeyHint = null;
        provider.Status = "unchecked";
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(ModelsEvents.KeyChanged, "provider", id.ToString(), provider.Name), cancellationToken);
        return ToDto(provider, await db.ModelEntries.CountAsync(m => m.ProviderId == id, cancellationToken));
    }

    public async Task<ProviderTestResult> TestAsync(Guid id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        var result = await probe.CheckAsync(provider.Protocol, provider.BaseUrl, vault.Unprotect(provider.KeyCipher), RequiresKey(provider.Protocol), cancellationToken);
        provider.Status = result.Ok ? "ok" : "error";
        provider.LastCheckAt = time.GetUtcNow();
        provider.LastLatencyMs = result.Ok ? result.LatencyMs : null;
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>Asks the provider which models it serves and adds the new ones (switched off: a person decides which to offer and at what price).</summary>
    public async Task<IReadOnlyList<ModelDto>> SyncAsync(Guid id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        var codes = await probe.ListModelsAsync(provider.Protocol, provider.BaseUrl, vault.Unprotect(provider.KeyCipher), RequiresKey(provider.Protocol), cancellationToken);
        var known = await db.ModelEntries.Where(m => m.ProviderId == id).Select(m => m.Code).ToListAsync(cancellationToken);
        foreach (var code in codes.Except(known, StringComparer.Ordinal).Take(200))
        {
            if (code.Length is > 0 and <= 120)
            {
                db.ModelEntries.Add(new AiModel { Id = Guid.NewGuid(), ProviderId = id, Code = code, DisplayName = code.Length > 80 ? code[..80] : code, Enabled = false,
                    Kind = code.Contains("embed", StringComparison.OrdinalIgnoreCase) ? "embedding" : "chat" });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return await ListModelsAsync(includeDisabled: true, cancellationToken);
    }

    public async Task<IReadOnlyList<ModelDto>> ListModelsAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        var rows = await (from m in db.ModelEntries.AsNoTracking()
                          join p in db.Providers.AsNoTracking() on m.ProviderId equals p.Id
                          where includeDisabled || (m.Enabled && p.Enabled)
                          orderby p.SortOrder, m.Kind descending, m.DisplayName, m.Id
                          select new { Model = m, Provider = p.Name }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => ToDto(r.Model, r.Provider))];
    }

    public async Task<ModelDto> UpdateModelAsync(Guid id, UpdateModelRequest request, CancellationToken cancellationToken)
    {
        var model = await db.ModelEntries.SingleOrDefaultAsync(m => m.Id == id, cancellationToken) ?? throw ApiException.NotFound("The model");
        var validator = new Validator();
        if (request.DisplayName is not null)
        {
            validator.Text("displayName", request.DisplayName, 80);
        }
        validator.ThrowIfInvalid();
        db.ExpectVersion(model, request.Version);
        model.DisplayName = request.DisplayName?.Trim() ?? model.DisplayName;
        if (request.IsDefault == true)
        {
            if (model.Kind != "chat" || !(request.Enabled ?? model.Enabled))
            {
                throw ApiException.Conflict(ErrorCodes.InvalidState, "Only an enabled chat model can be the default.");
            }
            await db.ModelEntries.Where(m => m.IsDefault && m.Id != id).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDefault, false), cancellationToken);
            model.IsDefault = true;
        }
        if (request.Enabled is { } enabled)
        {
            if (!enabled && model.IsDefault && request.IsDefault != true)
            {
                throw ApiException.Conflict(ErrorCodes.InvalidState, "Choose another default model before switching this one off.");
            }
            model.Enabled = enabled;
        }
        await db.SaveChangesAsync(cancellationToken);
        var provider = await db.Providers.AsNoTracking().Where(p => p.Id == model.ProviderId).Select(p => p.Name).SingleAsync(cancellationToken);
        var dto = ToDto(model, provider);
        await audit.RecordAsync(new AuditRecord(ModelsEvents.ModelUpdated, "model", id.ToString(), model.DisplayName), cancellationToken);
        return dto;
    }

    private async Task EnsureDefaultAsync(Guid? except, CancellationToken cancellationToken)
    {
        if (await db.ModelEntries.AnyAsync(m => m.IsDefault, cancellationToken))
        {
            return;
        }
        var next = await (from m in db.ModelEntries join p in db.Providers on m.ProviderId equals p.Id
                          where m.Enabled && m.Kind == "chat" && p.Enabled && p.Id != except
                          orderby p.SortOrder, m.DisplayName, m.Id select m).FirstOrDefaultAsync(cancellationToken);
        if (next is not null)
        {
            next.IsDefault = true;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<AiProvider> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Providers.SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw ApiException.NotFound("The provider");

    private static void ValidateUrl(Validator validator, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) && !(Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            validator.Add("baseUrl", "Use an absolute http or https address.");
        }
    }

    internal static ProviderDto ToDto(AiProvider p, int models)
    {
        var requiresKey = RequiresKey(p.Protocol);
        var keySet = p.KeyCipher is not null;
        var status = requiresKey && !keySet && !ProviderRouting.IsDemoHost(p.BaseUrl) ? "no_key" : p.Status;
        return new ProviderDto(p.Id, p.Name, p.Protocol, p.BaseUrl, requiresKey, keySet, p.KeyHint, p.Enabled, status, p.LastLatencyMs, p.LastCheckAt, models, p.Version);
    }

    internal static ModelDto ToDto(AiModel m, string providerName) =>
        new(m.Id, m.ProviderId, providerName, m.Code, m.DisplayName, m.Kind, m.ContextTokens, m.PriceIn, m.PriceOut, m.PriceCache, Split(m.Caps), Split(m.Efforts), m.Note, m.Enabled, m.IsDefault, m.Version);

    internal static string[] Split(string? value) => string.IsNullOrEmpty(value) ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
