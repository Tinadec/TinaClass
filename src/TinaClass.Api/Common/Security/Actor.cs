namespace TinaClass.Api.Common.Security;

public enum ActorType
{
    User,
    ApiKey,
    System,
}

/// <summary>
/// Who is making the request: a signed-in user, an API key acting for the user who made it (with the permissions of its
/// scopes that the user's role still has), or the API itself (background jobs). <see cref="Id"/> is the user's id in both
/// of the first two cases.
/// </summary>
public sealed record Actor(ActorType Type, Guid? Id, string Name, string? RoleId, IReadOnlySet<string> Permissions, Guid? SessionId)
{
    public static Actor System { get; } = new(ActorType.System, null, "System", null, new HashSet<string>(), null);

    public bool Can(string permission) => Type == ActorType.System || Permissions.Contains(permission);
}

/// <summary>The actor of the current request, loaded once per request. Implemented by the auth feature.</summary>
public interface IActorContext
{
    /// <summary>The signed-in actor, or null when the request is anonymous (or there is no request).</summary>
    Task<Actor?> GetAsync(CancellationToken cancellationToken);

    /// <summary>The signed-in actor; 401 when anonymous.</summary>
    Task<Actor> RequireAsync(CancellationToken cancellationToken);

    string? Ip { get; }

    /// <summary>The client's description of itself: the <c>X-Client-Device</c> header, else the User-Agent.</summary>
    string? Device { get; }
}

/// <summary>Claim types the API puts in its tokens and that the API-key handler sets.</summary>
public static class AppClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Session = "sid";
    public const string Kind = "kind";
    public const string Scope = "scope";
    public const string ApiKeyKind = "api_key";
}
