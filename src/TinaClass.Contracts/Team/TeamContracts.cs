namespace TinaClass.Contracts.Team;

public sealed record MemberDto(Guid Id, string Name, string Email, string RoleId, string Status, DateTimeOffset? JoinedAt);

public sealed record InviteRequest(string Email, string RoleId);

/// <summary>An invitation. The token is shown once (it is the link); only its hash is stored.</summary>
public sealed record InviteDto(Guid Id, string Email, string RoleId, DateTimeOffset ExpiresAt, string? Token);

/// <summary>The invitee's answer to the link: the token from the invitation, the name to show and a password.</summary>
public sealed record AcceptInviteRequest(string Token, string Name, string Password);

public sealed record UpdateMemberRequest(string? RoleId, bool? Disabled);

public sealed record AuditEntryDto(Guid Id, string ActorName, string Action, string TargetKind, string TargetId, string Detail, DateTimeOffset At);

public static class TeamEvents
{
    public const string MemberInvited = "member.invited";
    public const string MemberJoined = "member.joined";
}
