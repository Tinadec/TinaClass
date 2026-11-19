using TinaClass.App.Common.Sample;
using TinaClass.Contracts.Sample;
using TinaClass.Contracts.Team;

namespace TinaClass.App.Features.Team;

/// <summary>The five members of 栖云科技, one pending invitation and a few audit entries (Contracts/Team/TeamSample) in memory.</summary>
public sealed class SampleTeamService : ITeamService
{
    private readonly List<MemberDto> _members =
    [
        .. SampleCore.People.Select((p, i) => new MemberDto(p.Id, p.Name, p.Email, p.Role, "active", SampleClock.At(120 - 9 * i, 9, 0))),
        new MemberDto(Guid.NewGuid(), "xu.mingzhe", TeamSample.InvitedEmail, "member", "invited", null),
    ];

    public Task<IReadOnlyList<MemberDto>> MembersAsync(CancellationToken cancellation = default) => Task.FromResult<IReadOnlyList<MemberDto>>([.. _members]);

    public Task<InviteDto> InviteAsync(string email, string roleId, CancellationToken cancellation = default)
    {
        var invite = new InviteDto(Guid.NewGuid(), email, roleId, SampleClock.Now.AddDays(7), "sample-invite-token");
        _members.Add(new MemberDto(invite.Id, email.Split('@')[0], email, roleId, "invited", null));
        return Task.FromResult(invite);
    }

    public Task<MemberDto> UpdateAsync(Guid memberId, string? roleId, bool? disabled, CancellationToken cancellation = default)
    {
        var index = _members.FindIndex(m => m.Id == memberId);
        var member = _members[index] with { RoleId = roleId ?? _members[index].RoleId, Status = disabled is { } off ? (off ? "disabled" : "active") : _members[index].Status };
        _members[index] = member;
        return Task.FromResult(member);
    }

    public Task<IReadOnlyList<AuditEntryDto>> AuditAsync(CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<AuditEntryDto>>([.. TeamSample.Audit.Select(a => new AuditEntryDto(Guid.NewGuid(), SampleCore.NameOf(a.Actor), a.Action, a.TargetKind, string.Empty, a.Detail, SampleClock.At(a.DaysAgo, a.Hour, 10)))
            .OrderByDescending(a => a.At)]);
}
