using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Team;
using TinaClass.Api.Features.Team.Data;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Team;

namespace TinaClass.Api.Tests.Team;

public sealed class TeamTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(string role) => await api.SignInAsync((await api.CreateUserAsync(role)).Email);

    private async Task<HttpResponseMessage> AcceptAsync(string token, string name = "Newcomer", string password = ApiFactory.Password)
    {
        using var anonymous = api.CreateClient();
        return await anonymous.PostJsonAsync(ApiRoutes.Invites + "/accept", new AcceptInviteRequest(token, name, password));
    }

    private async Task<InviteDto> InviteAsync(HttpClient client, string? email = null, string role = Roles.Member) =>
        await Expect.JsonAsync<InviteDto>(await client.PostJsonAsync(ApiRoutes.Invites, new InviteRequest(email ?? ApiFactory.NewEmail("invitee"), role)), HttpStatusCode.Created);

    [Fact]
    public async Task Each_Role_Gets_The_Answer_Its_Permissions_Allow()
    {
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Members, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        await Expect.ProblemAsync(await anonymous.PostJsonAsync(ApiRoutes.Invites, new InviteRequest("a@example.com", Roles.Member)), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        using var viewer = await ClientAsync(Roles.Viewer);
        using var member = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        foreach (var client in new[] { viewer, member })
        {
            await Expect.ProblemAsync(await client.GetAsync(ApiRoutes.Members, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
            await Expect.ProblemAsync(await client.GetAsync(ApiRoutes.Audit, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
            await Expect.ProblemAsync(await client.PostJsonAsync(ApiRoutes.Invites, new InviteRequest("a@example.com", Roles.Member)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
            await Expect.ProblemAsync(await client.PatchJsonAsync($"{ApiRoutes.Members}/{Guid.NewGuid()}", new UpdateMemberRequest(Roles.Admin, null)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
            await Expect.ProblemAsync(await client.SendDeleteAsync($"{ApiRoutes.Members}/{Guid.NewGuid()}"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(ApiRoutes.Members, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(ApiRoutes.Audit, Ct)).StatusCode);
        await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{Guid.NewGuid()}", new UpdateMemberRequest(Roles.Admin, null)), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Members_Are_Listed_With_Their_Role_Status_And_Pending_Invitations()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var member = await api.CreateUserAsync(Roles.Member, name: "Mia");
        var invite = await InviteAsync(admin, "pending@example.com", Roles.Viewer);
        var list = await admin.GetJsonAsync<IReadOnlyList<MemberDto>>(ApiRoutes.Members);
        var mia = Assert.Single(list, m => m.Id == member.Id);
        Assert.Equal(("Mia", member.Email, Roles.Member, "active"), (mia.Name, mia.Email, mia.RoleId, mia.Status));
        Assert.NotNull(mia.JoinedAt);
        var pending = Assert.Single(list, m => m.Id == invite.Id);
        Assert.Equal(("pending@example.com", Roles.Viewer, "invited", null), (pending.Email, pending.RoleId, pending.Status, pending.JoinedAt));
    }

    [Fact]
    public async Task A_Role_Change_Takes_Effect_At_Once_And_Only_An_Owner_Makes_Or_Changes_An_Owner()
    {
        using var admin = await ClientAsync(Roles.Admin);
        using var owner = await ClientAsync(Roles.Owner);
        var user = await api.CreateUserAsync(Roles.Member);
        using var asUser = await api.SignInAsync(user.Email);
        // Sharing a conversation needs chat.share: a member has it, a viewer does not. The conversation is still empty, so a member
        // gets past the permission and meets the "nothing to share yet" rule (409); a viewer is stopped before that (403).
        var chat = await Expect.JsonAsync<ConversationDto>(await asUser.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest("Mine", null, null, null)), HttpStatusCode.Created);
        var share = new CreateShareRequest(null);
        await Expect.ProblemAsync(await asUser.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/share", share), HttpStatusCode.Conflict, ErrorCodes.InvalidState);

        var updated = await Expect.JsonAsync<MemberDto>(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{user.Id}", new UpdateMemberRequest(Roles.Viewer, null)));
        Assert.Equal(Roles.Viewer, updated.RoleId);
        await Expect.ProblemAsync(await asUser.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/share", share), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Contains(api.Audit.Records, r => r.Action == TeamAuditActions.MemberRoleChanged && r.ResourceId == user.Id.ToString());

        var fields = await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{user.Id}", new UpdateMemberRequest("emperor", null)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["roleId"], fields);
        await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{user.Id}", new UpdateMemberRequest(Roles.Owner, null)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        var boss = await api.CreateUserAsync(Roles.Owner);
        await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{boss.Id}", new UpdateMemberRequest(Roles.Member, null)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{boss.Id}", new UpdateMemberRequest(null, true)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await admin.SendDeleteAsync($"{ApiRoutes.Members}/{boss.Id}"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(Roles.Owner, (await Expect.JsonAsync<MemberDto>(await owner.PatchJsonAsync($"{ApiRoutes.Members}/{user.Id}", new UpdateMemberRequest(Roles.Owner, null)))).RoleId);
    }

    [Fact]
    public async Task A_Disabled_Member_Cannot_Sign_In_Or_Use_Their_Token_And_Can_Be_Turned_On_Again()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var user = await api.CreateUserAsync(Roles.Member);
        using var asUser = await api.SignInAsync(user.Email);
        Assert.Equal(HttpStatusCode.OK, (await asUser.GetAsync(ApiRoutes.Conversations, Ct)).StatusCode);

        var off = await Expect.JsonAsync<MemberDto>(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{user.Id}", new UpdateMemberRequest(null, true)));
        Assert.Equal("disabled", off.Status);
        await Expect.ProblemAsync(await asUser.GetAsync(ApiRoutes.Conversations, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        await Assert.ThrowsAnyAsync<Exception>(() => api.SignInAsync(user.Email));
        Assert.Contains(api.Audit.Records, r => r.Action == TeamAuditActions.MemberDisabled && r.ResourceId == user.Id.ToString());

        Assert.Equal("active", (await Expect.JsonAsync<MemberDto>(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{user.Id}", new UpdateMemberRequest(null, false)))).Status);
        using var again = await api.SignInAsync(user.Email);
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync(ApiRoutes.Conversations, Ct)).StatusCode);
        Assert.Contains(api.Audit.Records, r => r.Action == TeamAuditActions.MemberEnabled && r.ResourceId == user.Id.ToString());

        // Removing a person turns the account off (twice is fine); nobody turns off their own.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendDeleteAsync($"{ApiRoutes.Members}/{user.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendDeleteAsync($"{ApiRoutes.Members}/{user.Id}")).StatusCode);
        var self = await api.CreateUserAsync(Roles.Admin);
        using var asSelf = await api.SignInAsync(self.Email);
        await Expect.ProblemAsync(await asSelf.SendDeleteAsync($"{ApiRoutes.Members}/{self.Id}"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await asSelf.PatchJsonAsync($"{ApiRoutes.Members}/{self.Id}", new UpdateMemberRequest(null, true)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task An_Invitation_Is_Checked_Mailed_Once_And_Only_Its_Hash_Is_Kept()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var fields = await Expect.ProblemAsync(await admin.PostJsonAsync(ApiRoutes.Invites, new InviteRequest("not an email", Roles.Member)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["email"], fields);
        Assert.Equal(["roleId"], await Expect.ProblemAsync(await admin.PostJsonAsync(ApiRoutes.Invites, new InviteRequest("ok@example.com", "emperor")), HttpStatusCode.BadRequest, ErrorCodes.Validation));
        await Expect.ProblemAsync(await admin.PostJsonAsync(ApiRoutes.Invites, new InviteRequest("ok@example.com", Roles.Owner)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        var existing = await api.CreateUserAsync(Roles.Member);
        await Expect.ProblemAsync(await admin.PostJsonAsync(ApiRoutes.Invites, new InviteRequest(existing.Email.ToUpperInvariant(), Roles.Member)), HttpStatusCode.Conflict, ErrorCodes.Conflict);

        var email = ApiFactory.NewEmail("new");
        var invite = await InviteAsync(admin, email, Roles.Viewer);
        Assert.False(string.IsNullOrEmpty(invite.Token));
        Assert.Equal((email, Roles.Viewer), (invite.Email, invite.RoleId));
        Assert.Equal(7, Math.Round((invite.ExpiresAt - api.Time.GetUtcNow()).TotalDays));
        var mail = Assert.Single(api.Email.Messages, m => m.To == email);
        Assert.Contains("tinaclass://accept-invite?token=" + invite.Token, mail.Body);

        var stored = await api.InScopeAsync(async sp => await sp.GetRequiredService<TeamDbContext>().Invites.AsNoTracking().SingleAsync(i => i.Id == invite.Id, Ct));
        Assert.NotEqual(invite.Token, stored.TokenHash);
        Assert.Equal(Secrets.Hash(invite.Token!), stored.TokenHash);
        Assert.DoesNotContain(api.Events.Events, e => e.Json.Contains(invite.Token!, StringComparison.Ordinal));
        Assert.Contains(api.Events.Events, e => e.Type == TeamEvents.MemberInvited && e.Json.Contains(email));
        Assert.Contains(api.Audit.Records, r => r.Action == TeamAuditActions.MemberInvited && r.ResourceId == invite.Id.ToString());
        Assert.DoesNotContain(api.Audit.Records, r => (r.ResourceName ?? "").Contains(invite.Token!, StringComparison.Ordinal));
        Assert.DoesNotContain(invite.Token!, await (await admin.GetAsync(ApiRoutes.Members, Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accepting_Creates_The_Account_Once_And_A_New_Invitation_Replaces_The_Old_Link()
    {
        using var admin = await ClientAsync(Roles.Admin);
        await Expect.ProblemAsync(await AcceptAsync("no-such-token"), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var email = ApiFactory.NewEmail("join");
        var first = await InviteAsync(admin, email, Roles.Member);
        var second = await InviteAsync(admin, email, Roles.Viewer);
        Assert.Single(await admin.GetJsonAsync<IReadOnlyList<MemberDto>>(ApiRoutes.Members), m => m.Email == email);
        await Expect.ProblemAsync(await AcceptAsync(first.Token!), HttpStatusCode.NotFound, ErrorCodes.NotFound);

        // A weak password does not use the invitation up.
        Assert.Equal(["password"], await Expect.ProblemAsync(await AcceptAsync(second.Token!, "Joiner", "short"), HttpStatusCode.BadRequest, ErrorCodes.Validation));
        var joined = await Expect.JsonAsync<MemberDto>(await AcceptAsync(second.Token!, "Joiner", "a-fine-password-1"));
        Assert.Equal(("Joiner", email, Roles.Viewer, "active"), (joined.Name, joined.Email, joined.RoleId, joined.Status));
        using var asJoiner = await api.SignInAsync(email, "a-fine-password-1");
        Assert.Equal(HttpStatusCode.OK, (await asJoiner.GetAsync(ApiRoutes.Conversations, Ct)).StatusCode);
        await Expect.ProblemAsync(await AcceptAsync(second.Token!), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.DoesNotContain(await admin.GetJsonAsync<IReadOnlyList<MemberDto>>(ApiRoutes.Members), m => m.Status == "invited" && m.Email == email);
        Assert.Contains(api.Events.Events, e => e.Type == TeamEvents.MemberJoined && e.Json.Contains(email));
        Assert.Contains(api.Audit.Records, r => r.Action == TeamAuditActions.MemberJoined && r.ResourceName == email);
    }

    [Fact]
    public async Task A_Pending_Invitation_Can_Change_Role_Or_Be_Revoked()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var invite = await InviteAsync(admin);
        var changed = await Expect.JsonAsync<MemberDto>(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{invite.Id}", new UpdateMemberRequest(Roles.Admin, null)));
        Assert.Equal((Roles.Admin, "invited"), (changed.RoleId, changed.Status));
        await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{invite.Id}", new UpdateMemberRequest(Roles.Owner, null)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(["roleId"], await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Members}/{invite.Id}", new UpdateMemberRequest("emperor", null)), HttpStatusCode.BadRequest, ErrorCodes.Validation));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendDeleteAsync($"{ApiRoutes.Members}/{invite.Id}")).StatusCode);
        await Expect.ProblemAsync(await AcceptAsync(invite.Token!), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.DoesNotContain(await admin.GetJsonAsync<IReadOnlyList<MemberDto>>(ApiRoutes.Members), m => m.Id == invite.Id);
        Assert.Contains(api.Audit.Records, r => r.Action == TeamAuditActions.InviteRevoked && r.ResourceId == invite.Id.ToString());
    }

    [Fact]
    public async Task The_Audit_Log_Holds_Every_Features_Records_Newest_First_In_Pages_And_Can_Be_Filtered()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var author = await api.CreateUserAsync(Roles.Member, name: "Author Ann");
        using var asAuthor = await api.SignInAsync(author.Email);
        // Another feature's record: deleting a conversation is audited by the chat feature.
        var chat = await Expect.JsonAsync<ConversationDto>(await asAuthor.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest("Audited", null, null, null)), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await asAuthor.SendDeleteAsync($"{ApiRoutes.Conversations}/{chat.Id}")).StatusCode);
        await InviteAsync(admin);

        var created = await admin.GetJsonAsync<Paged<AuditEntryDto>>($"{ApiRoutes.Audit}?action=chat.conversation_deleted");
        var entry = Assert.Single(created.Items, e => e.TargetId == chat.Id.ToString());
        Assert.Equal(("Author Ann", "conversation"), (entry.ActorName, entry.TargetKind));
        Assert.All(created.Items, e => Assert.StartsWith("chat.conversation_deleted", e.Action, StringComparison.Ordinal));
        var members = await admin.GetJsonAsync<Paged<AuditEntryDto>>($"{ApiRoutes.Audit}?action=member.");
        Assert.All(members.Items, e => Assert.StartsWith("member.", e.Action, StringComparison.Ordinal));

        var one = await admin.GetJsonAsync<Paged<AuditEntryDto>>($"{ApiRoutes.Audit}?page=1&pageSize=1");
        var two = await admin.GetJsonAsync<Paged<AuditEntryDto>>($"{ApiRoutes.Audit}?page=2&pageSize=1");
        Assert.Equal((1, 1), (one.Items.Count, two.Items.Count));
        Assert.True(one.Total >= 2);
        Assert.NotEqual(one.Items[0].Id, two.Items[0].Id);
        Assert.True(one.Items[0].At >= two.Items[0].At);
    }
}

/// <summary>Needs a workspace of its own: it counts the owners.</summary>
public sealed class TeamOwnerTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_Last_Active_Owner_Can_Be_Neither_Demoted_Nor_Turned_Off()
    {
        var first = await api.CreateUserAsync(Roles.Owner);
        using var asFirst = await api.SignInAsync(first.Email);
        await Expect.ProblemAsync(await asFirst.PatchJsonAsync($"{ApiRoutes.Members}/{first.Id}", new UpdateMemberRequest(Roles.Admin, null)), HttpStatusCode.Conflict, ErrorCodes.LastOwner);

        var second = await api.CreateUserAsync(Roles.Owner);
        using var asSecond = await api.SignInAsync(second.Email);
        // With two owners one may turn the other off, but then the remaining one is the last.
        Assert.Equal("disabled", (await Expect.JsonAsync<MemberDto>(await asFirst.PatchJsonAsync($"{ApiRoutes.Members}/{second.Id}", new UpdateMemberRequest(null, true)))).Status);
        await Expect.ProblemAsync(await asFirst.PatchJsonAsync($"{ApiRoutes.Members}/{first.Id}", new UpdateMemberRequest(Roles.Member, null)), HttpStatusCode.Conflict, ErrorCodes.LastOwner);
        var third = await api.CreateUserAsync(Roles.Owner);
        using var asThird = await api.SignInAsync(third.Email);
        Assert.Equal(Roles.Admin, (await Expect.JsonAsync<MemberDto>(await asThird.PatchJsonAsync($"{ApiRoutes.Members}/{first.Id}", new UpdateMemberRequest(Roles.Admin, null)))).RoleId);
        // The turned-off owner does not count: the third is the last again.
        await Expect.ProblemAsync(await asThird.PatchJsonAsync($"{ApiRoutes.Members}/{third.Id}", new UpdateMemberRequest(Roles.Member, null)), HttpStatusCode.Conflict, ErrorCodes.LastOwner);
        Assert.NotNull(asSecond);
        Assert.Equal(HttpStatusCode.OK, (await asThird.GetAsync(ApiRoutes.Members, Ct)).StatusCode);
    }
}

/// <summary>Moves the clock, so it has a workspace of its own.</summary>
public sealed class TeamInviteExpiryTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task An_Invitation_Stops_Working_After_Its_Days_And_Leaves_The_List()
    {
        using var admin = await api.SignInAsync((await api.CreateUserAsync(Roles.Admin)).Email);
        var invite = await Expect.JsonAsync<InviteDto>(await admin.PostJsonAsync(ApiRoutes.Invites, new InviteRequest(ApiFactory.NewEmail("late"), Roles.Member)), HttpStatusCode.Created);
        api.Time.Advance(TimeSpan.FromDays(8));
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.PostJsonAsync(ApiRoutes.Invites + "/accept", new AcceptInviteRequest(invite.Token!, "Late", ApiFactory.Password)), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        using var later = await api.SignInAsync((await api.CreateUserAsync(Roles.Admin)).Email);
        Assert.DoesNotContain(await later.GetJsonAsync<IReadOnlyList<MemberDto>>(ApiRoutes.Members), m => m.Id == invite.Id);
    }
}
