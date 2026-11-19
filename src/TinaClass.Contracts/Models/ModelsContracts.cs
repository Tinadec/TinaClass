namespace TinaClass.Contracts.Models;

/// <summary>Wire protocols of a provider. <see cref="Echo"/> is built in (offline, no key); the others are adapters you add or configure.</summary>
public static class ProviderProtocols
{
    public const string Echo = "echo";
    public const string OpenAiCompatible = "openai-compatible";
    public const string Messages = "messages";
    public const string Local = "local";
}

/// <summary>GET /providers. The key itself is never returned: only whether one is set and its last four characters.</summary>
public sealed record ProviderDto(Guid Id, string Name, string Protocol, string BaseUrl, bool RequiresKey, bool KeySet, string? KeyHint,
    bool Enabled, string Status, int? LastLatencyMs, DateTimeOffset? LastCheckAt, int ModelCount, int Version);

public sealed record CreateProviderRequest(string Name, string Protocol, string BaseUrl);

public sealed record UpdateProviderRequest(string? Name, string? BaseUrl, bool? Enabled, int Version);

/// <summary>PUT /providers/{id}/key (needs keys.manage).</summary>
public sealed record SetKeyRequest(string Key);

public sealed record ProviderTestResult(bool Ok, int LatencyMs, string? Error);

/// <summary>Prices are minor units per million tokens (see <c>Money</c>).</summary>
public sealed record ModelDto(Guid Id, Guid ProviderId, string ProviderName, string Code, string DisplayName, string Kind, int ContextTokens,
    long PriceIn, long PriceOut, long PriceCache, IReadOnlyList<string> Caps, IReadOnlyList<string> Efforts, string? Note, bool Enabled, bool IsDefault, int Version);

public sealed record UpdateModelRequest(string? DisplayName, bool? Enabled, bool? IsDefault, int Version);

public static class ModelsEvents
{
    public const string ProviderCreated = "provider.created";
    public const string ProviderUpdated = "provider.updated";
    public const string ProviderDeleted = "provider.deleted";
    public const string KeyChanged = "provider.key_changed";
    public const string ModelUpdated = "model.updated";
}
