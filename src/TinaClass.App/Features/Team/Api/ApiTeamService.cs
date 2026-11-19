using TinaClass.App.Common.Api;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Team;

namespace TinaClass.App.Features.Team.Api;

/// <summary>The team of the API (<c>/members</c>, <c>/invites</c>, <c>/audit</c>).</summary>
public sealed class ApiTeamService(ApiClient api) : ITeamService
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<IReadOnlyList<MemberDto>> MembersAsync(CancellationToken cancellation = default) => api.GetAsync(ApiRoutes.Members, Json.IReadOnlyListMemberDto, cancellation);

    public Task<InviteDto> InviteAsync(string email, string roleId, CancellationToken cancellation = default) =>
        api.PostAsync(ApiRoutes.Invites, new InviteRequest(email, roleId), Json.InviteRequest, Json.InviteDto, cancellation);

    public Task<MemberDto> UpdateAsync(Guid memberId, string? roleId, bool? disabled, CancellationToken cancellation = default) =>
        api.PatchAsync($"{ApiRoutes.Members}/{memberId}", new UpdateMemberRequest(roleId, disabled), Json.UpdateMemberRequest, Json.MemberDto, cancellation);

    public async Task<IReadOnlyList<AuditEntryDto>> AuditAsync(CancellationToken cancellation = default) =>
        (await api.GetAsync($"{ApiRoutes.Audit}?pageSize=50", Json.PagedAuditEntryDto, cancellation)).Items;
}
