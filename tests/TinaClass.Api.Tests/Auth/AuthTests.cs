using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Auth;
using TinaClass.Api.Features.Auth.Data;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Auth;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Auth;

public sealed class AuthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signing_In_Returns_Tokens_That_Open_The_Account()
    {
        var user = await api.CreateUserAsync(Roles.Owner, name: "Ada Admin");
        using var client = await api.SignInAsync(user.Email);
        var me = await client.GetJsonAsync<CurrentUser>(ApiRoutes.Me);
        Assert.Equal((user.Id, user.Email, "Ada Admin", Roles.Owner), (me.Id, me.Email, me.Name, me.RoleId));
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), me.Permissions);
        Assert.Contains(api.Audit.Records, r => r.Action == AuthAuditActions.SignedIn && r.ResourceId == user.Id.ToString());
    }

    [Fact]
    public async Task Anonymous_And_Bad_Tokens_Get_A_401_Problem_With_A_Code()
    {
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.ApiKeys, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        using var garbage = api.Authorized("not-a-token");
        await Expect.ProblemAsync(await garbage.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        using var unknownKey = api.Authorized("ak_live_00000000000000000000000000000000");
        await Expect.ProblemAsync(await unknownKey.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task A_Role_Without_The_Permission_Gets_A_403_Problem_With_A_Code()
    {
        var viewer = await api.CreateUserAsync(Roles.Viewer);
        using var client = await api.SignInAsync(viewer.Email);
        await Expect.ProblemAsync(await client.GetAsync(ApiRoutes.ApiKeys, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await client.PostJsonAsync(ApiRoutes.ApiKeys, new CreateApiKeyRequest("k", [Permissions.ApiKeysManage])),
            HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task A_Wrong_Password_And_An_Unknown_Email_Get_The_Same_Answer()
    {
        var user = await api.CreateUserAsync(Roles.Member);
        using var client = api.CreateClient();
        var wrong = await client.PostJsonAsync(ApiRoutes.Auth + "/login", new LoginRequest(user.Email, "wrong password", false));
        var unknown = await client.PostJsonAsync(ApiRoutes.Auth + "/login", new LoginRequest("nobody@example.com", "whatever", false));
        await Expect.ProblemAsync(wrong, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        await Expect.ProblemAsync(unknown, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        Assert.Equal(WithoutTrace(await wrong.Content.ReadAsStringAsync(Ct)), WithoutTrace(await unknown.Content.ReadAsStringAsync(Ct)));
        Assert.Contains(api.Audit.Records, r => r.Action == AuthAuditActions.SignInFailed && r.ActorName == "nobody@example.com");
    }

    /// <summary>A problem body as comparable text: every field but the trace id, which is different on every request.</summary>
    private static string WithoutTrace(string json)
    {
        using var document = JsonDocument.Parse(json);
        return string.Join("|", document.RootElement.EnumerateObject().Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}"));
    }

    [Fact]
    public async Task Five_Wrong_Passwords_Block_Sign_In_For_A_While_Without_Saying_Why()
    {
        var user = await api.CreateUserAsync(Roles.Member);
        using var client = api.CreateClient();
        for (var i = 0; i < AuthService.MaxFailedSignIns; i++)
        {
            await Expect.ProblemAsync(await client.PostJsonAsync(ApiRoutes.Auth + "/login", new LoginRequest(user.Email, "wrong password", false)),
                HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        }
        // The right password is refused while the lock holds, with the same answer as any other failure.
        await Expect.ProblemAsync(await client.PostJsonAsync(ApiRoutes.Auth + "/login", new LoginRequest(user.Email, ApiFactory.Password, false)),
            HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);

        api.Time.Advance(AuthService.LockoutDuration + TimeSpan.FromMinutes(1));
        await Expect.StatusAsync(await client.PostJsonAsync(ApiRoutes.Auth + "/login", new LoginRequest(user.Email, ApiFactory.Password, false)), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_Tokens_Rotate_And_A_Reused_One_Ends_The_Session()
    {
        var user = await api.CreateUserAsync(Roles.Member);
        var first = await api.LoginAsync(user.Email);
        using var anonymous = api.CreateClient();
        var second = await Expect.JsonAsync<TokenPair>(await anonymous.PostJsonAsync(ApiRoutes.Auth + "/refresh", new RefreshRequest(first.RefreshToken)));
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        using (var client = api.Authorized(second.AccessToken))
        {
            await Expect.StatusAsync(await client.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.OK);
        }

        // The first token was already exchanged: using it again means someone else may hold it, so the whole session ends.
        await Expect.ProblemAsync(await anonymous.PostJsonAsync(ApiRoutes.Auth + "/refresh", new RefreshRequest(first.RefreshToken)),
            HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
        await Expect.ProblemAsync(await anonymous.PostJsonAsync(ApiRoutes.Auth + "/refresh", new RefreshRequest(second.RefreshToken)),
            HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
        using var revoked = api.Authorized(second.AccessToken);
        await Expect.ProblemAsync(await revoked.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task An_Expired_Access_Token_Is_Refused_And_A_Refresh_Gives_A_New_One()
    {
        var user = await api.CreateUserAsync(Roles.Member);
        var pair = await api.LoginAsync(user.Email);
        api.Time.Advance(TimeSpan.FromMinutes(16));
        using (var expired = api.Authorized(pair.AccessToken))
        {
            await Expect.ProblemAsync(await expired.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        }
        using var anonymous = api.CreateClient();
        var fresh = await Expect.JsonAsync<TokenPair>(await anonymous.PostJsonAsync(ApiRoutes.Auth + "/refresh", new RefreshRequest(pair.RefreshToken)));
        using var client = api.Authorized(fresh.AccessToken);
        await Expect.StatusAsync(await client.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_Out_Ends_The_Session()
    {
        var user = await api.CreateUserAsync(Roles.Member);
        var pair = await api.LoginAsync(user.Email);
        using var client = api.Authorized(pair.AccessToken);
        await Expect.StatusAsync(await client.PostJsonAsync(ApiRoutes.Auth + "/logout", new RefreshRequest(pair.RefreshToken)), HttpStatusCode.NoContent);
        await Expect.ProblemAsync(await client.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        await Expect.ProblemAsync(await client.PostJsonAsync(ApiRoutes.Auth + "/refresh", new RefreshRequest(pair.RefreshToken)),
            HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
        // Signing out twice, or with a token nobody knows, is not an error.
        await Expect.StatusAsync(await client.PostJsonAsync(ApiRoutes.Auth + "/logout", new RefreshRequest("rt_unknown")), HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_Disabled_Account_Is_Refused_At_Once()
    {
        var user = await api.CreateUserAsync(Roles.Member);
        using var client = await api.SignInAsync(user.Email);
        await Expect.StatusAsync(await client.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.OK);
        await api.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AuthDbContext>();
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Disabled, true), Ct);
            return 0;
        });
        await Expect.ProblemAsync(await client.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.PostJsonAsync(ApiRoutes.Auth + "/login", new LoginRequest(user.Email, ApiFactory.Password, false)),
            HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task Only_Hashes_Of_Tokens_And_Keys_Are_Stored()
    {
        var user = await api.CreateUserAsync(Roles.Admin);
        var pair = await api.LoginAsync(user.Email);
        using var client = api.Authorized(pair.AccessToken);
        var created = await Expect.JsonAsync<CreatedApiKey>(await client.PostJsonAsync(ApiRoutes.ApiKeys, new CreateApiKeyRequest("ci", [Permissions.ApiKeysManage])));

        var (sessionHashes, keyHashes, passwordHash) = await api.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AuthDbContext>();
            return (await db.Sessions.Where(s => s.UserId == user.Id).Select(s => s.TokenHash).ToListAsync(Ct),
                    await db.ApiKeys.Where(k => k.UserId == user.Id).Select(k => k.SecretHash).ToListAsync(Ct),
                    await db.Users.Where(u => u.Id == user.Id).Select(u => u.PasswordHash).SingleAsync(Ct));
        });
        Assert.Equal([Secrets.Hash(pair.RefreshToken)], sessionHashes);
        Assert.Equal([Secrets.Hash(created.Secret)], keyHashes);
        Assert.DoesNotContain(ApiFactory.Password, passwordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Api_Key_Is_Shown_Once_Authenticates_Within_Its_Scopes_And_Can_Be_Revoked()
    {
        var user = await api.CreateUserAsync(Roles.Admin);
        using var session = await api.SignInAsync(user.Email);
        var created = await Expect.JsonAsync<CreatedApiKey>(await session.PostJsonAsync(ApiRoutes.ApiKeys, new CreateApiKeyRequest("ci", [Permissions.ApiKeysManage])));
        Assert.StartsWith("ak_live_", created.Secret, StringComparison.Ordinal);
        Assert.StartsWith(created.Key.Prefix, created.Secret, StringComparison.Ordinal);
        Assert.NotEqual(created.Secret, created.Key.Prefix);

        var listed = await session.GetJsonAsync<IReadOnlyList<ApiKeyDto>>(ApiRoutes.ApiKeys);
        Assert.Equal([created.Key.Id], listed.Select(k => k.Id));

        using var key = api.Authorized(created.Secret);
        var me = await key.GetJsonAsync<CurrentUser>(ApiRoutes.Me);
        Assert.Equal(user.Id, me.Id);
        Assert.Equal([Permissions.ApiKeysManage], me.Permissions);
        // A key acts for its user, but only a signed-in person manages keys.
        await Expect.ProblemAsync(await key.GetAsync(ApiRoutes.ApiKeys, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);

        await Expect.StatusAsync(await session.DeleteAsync($"{ApiRoutes.ApiKeys}/{created.Key.Id}", Ct), HttpStatusCode.NoContent);
        await Expect.ProblemAsync(await key.GetAsync(ApiRoutes.Me, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        await Expect.ProblemAsync(await session.DeleteAsync($"{ApiRoutes.ApiKeys}/{created.Key.Id}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Key_Cannot_Have_More_Than_Its_Owner_May_Do_And_Is_Validated()
    {
        var member = await api.CreateUserAsync(Roles.Member);
        using var client = await api.SignInAsync(member.Email);
        // Every permission a member has is fine; one it lacks, or an unknown one, is not.
        var fields = await Expect.ProblemAsync(await client.PostJsonAsync(ApiRoutes.ApiKeys, new CreateApiKeyRequest("", ["settings.everything"])),
            HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["name", "scopes"], fields.Order(StringComparer.Ordinal));
        await Expect.ProblemAsync(await client.PostJsonAsync(ApiRoutes.ApiKeys, new CreateApiKeyRequest("empty", [])), HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [Fact]
    public async Task A_Key_Loses_Scopes_Its_Owners_Role_No_Longer_Has()
    {
        var admin = await api.CreateUserAsync(Roles.Owner);
        using var session = await api.SignInAsync(admin.Email);
        var created = await Expect.JsonAsync<CreatedApiKey>(await session.PostJsonAsync(ApiRoutes.ApiKeys, new CreateApiKeyRequest("all", Permissions.All)));
        await api.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AuthDbContext>();
            await db.Users.Where(u => u.Id == admin.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.RoleId, Roles.Viewer), Ct);
            return 0;
        });
        using var key = api.Authorized(created.Secret);
        var me = await key.GetJsonAsync<CurrentUser>(ApiRoutes.Me);
        Assert.Equal(Roles.PermissionsOf(Roles.Viewer).Order(StringComparer.Ordinal), me.Permissions);
    }

    [Fact]
    public async Task A_Write_Based_On_A_Stale_Row_Fails_On_Its_Version()
    {
        var user = await api.CreateUserAsync(Roles.Member);
        await api.LoginAsync(user.Email);
        await using var first = api.Services.CreateAsyncScope();
        await using var second = api.Services.CreateAsyncScope();
        var a = await first.ServiceProvider.GetRequiredService<AuthDbContext>().Sessions.FirstAsync(s => s.UserId == user.Id, Ct);
        var dbB = second.ServiceProvider.GetRequiredService<AuthDbContext>();
        var b = await dbB.Sessions.FirstAsync(s => s.UserId == user.Id, Ct);

        b.Device = "changed first";
        await dbB.SaveChangesAsync(Ct);
        Assert.Equal(1, b.Version);

        a.Device = "changed second";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.ServiceProvider.GetRequiredService<AuthDbContext>().SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task The_Cleanup_Removes_Old_Sessions_And_Keeps_Current_Ones()
    {
        var user = await api.CreateUserAsync(Roles.Member);
        var old = await api.LoginAsync(user.Email);
        api.Time.Advance(AuthService.SessionFor + TimeSpan.FromDays(2));
        var current = await api.LoginAsync(user.Email);

        var removed = await api.InScopeAsync(services => services.GetRequiredService<SessionCleaner>().RunAsync(Ct));
        Assert.True(removed >= 1);
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.PostJsonAsync(ApiRoutes.Auth + "/refresh", new RefreshRequest(old.RefreshToken)),
            HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
        await Expect.StatusAsync(await anonymous.PostJsonAsync(ApiRoutes.Auth + "/refresh", new RefreshRequest(current.RefreshToken)), HttpStatusCode.OK);
    }
}
