using TinaClass.Contracts.Team;

namespace TinaClass.App.Features.Team;

/// <summary>Members, invitations and the audit log of the workspace (design 4.1, <c>features.team</c>; the API owns the permissions, the app shows what the API allows).</summary>
public interface ITeamService
{
    /// <summary>The members, with the invited ones (status "invited") at the end.</summary>
    Task<IReadOnlyList<MemberDto>> MembersAsync(CancellationToken cancellation = default);

    Task<InviteDto> InviteAsync(string email, string roleId, CancellationToken cancellation = default);

    Task<MemberDto> UpdateAsync(Guid memberId, string? roleId, bool? disabled, CancellationToken cancellation = default);

    /// <summary>The newest audit entries (the first page).</summary>
    Task<IReadOnlyList<AuditEntryDto>> AuditAsync(CancellationToken cancellation = default);
}
