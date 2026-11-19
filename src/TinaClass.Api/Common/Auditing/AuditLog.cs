namespace TinaClass.Api.Common.Auditing;

/// <summary>
/// What happened, for the audit log. <see cref="Action"/> is one of the features' <c>…AuditActions</c> / <c>…Events</c> constants.
/// <see cref="ActorName"/> names the actor when nobody is signed in (a failed sign-in records the email tried).
/// </summary>
public sealed record AuditRecord(string Action, string ResourceType, string? ResourceId = null, string? ResourceName = null, string? ActorName = null);

/// <summary>
/// Records who did what. Every feature writes through this seam; a feature that stores the entries (an audit feature copied
/// from Atlas) replaces the default with <c>services.Replace</c>. Without one the records go nowhere (<see cref="NullAuditLog"/>).
/// </summary>
public interface IAuditLog
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}

internal sealed class NullAuditLog : IAuditLog
{
    public Task RecordAsync(AuditRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
}
