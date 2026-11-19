using System.Net;
using Avalonia.Headless.XUnit;
using TinaClass.App.Common.Api;
using TinaClass.Contracts;
using TinaClass.Contracts.Auth;
using TinaClass.Contracts.Common;
using Xunit;

namespace TinaClass.App.Tests.Api;

/// <summary>The API client against a fake server: sign-in, problem details, the token refresh, waiting for a sign-in, no answer.</summary>
public class ApiClientTests
{
    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private static (ApiClient Client, ApiSession Session, MemoryTokenStore Tokens) Client(HttpMessageHandler server)
    {
        var session = new ApiSession();
        var tokens = new MemoryTokenStore();
        return (new ApiClient(session, tokens, server), session, tokens);
    }

    [AvaloniaFact]
    public async Task Signing_in_opens_the_session_and_keeps_the_refresh_token_only_when_asked()
    {
        var (client, session, tokens) = Client(new FakeApi());
        var user = await client.SignInAsync(FakeApi.Email, FakeApi.Password, remember: false);
        Assert.Equal("Ada", user.Name);
        Assert.True(session.IsSignedIn);
        Assert.Null(tokens.Load());

        await client.SignOutAsync();
        Assert.False(session.IsSignedIn);
        await client.SignInAsync(FakeApi.Email, FakeApi.Password, remember: true);
        Assert.Equal("refresh-1", tokens.Load());
        await client.SignOutAsync();
        Assert.Null(tokens.Load());
    }

    [AvaloniaFact]
    public async Task A_remembered_session_resumes_and_a_refused_one_does_not()
    {
        var fake = new FakeApi();
        var (client, session, tokens) = Client(fake);
        tokens.Save("refresh-1");
        Assert.Equal("Ada", (await client.ResumeAsync())!.Name);
        Assert.True(session.IsSignedIn);

        var (other, otherSession, otherTokens) = Client(new FakeApi { RefuseRefresh = true });
        otherTokens.Save("refresh-gone");
        Assert.Null(await other.ResumeAsync());
        Assert.False(otherSession.IsSignedIn);
        Assert.Null(otherTokens.Load());
    }

    [AvaloniaFact]
    public async Task A_wrong_password_is_a_service_exception_with_the_api_code_and_a_readable_message()
    {
        var (client, session, _) = Client(new FakeApi());
        var error = await Assert.ThrowsAsync<ServiceException>(() => client.SignInAsync(FakeApi.Email, "wrong", remember: false));
        Assert.Equal(ErrorCodes.InvalidCredentials, error.Code);
        Assert.Contains("email or password", error.Message, StringComparison.Ordinal);
        Assert.False(session.IsSignedIn);
    }

    [AvaloniaFact]
    public async Task A_problem_details_code_and_its_fields_come_through()
    {
        var fake = new FakeApi();
        var (client, _, _) = Client(fake);
        await client.SignInAsync(FakeApi.Email, FakeApi.Password, remember: false);
        fake.Failure = (HttpStatusCode.BadRequest, ErrorCodes.Validation);
        var error = await Assert.ThrowsAsync<ServiceException>(() => client.GetAsync(ApiRoutes.Me, ContractsJsonContext.Default.CurrentUser));
        Assert.Equal(ErrorCodes.Validation, error.Code);
    }

    [AvaloniaFact]
    public async Task An_expired_access_token_is_refreshed_once_and_the_call_retried()
    {
        var fake = new FakeApi();
        var (client, _, tokens) = Client(fake);
        await client.SignInAsync(FakeApi.Email, FakeApi.Password, remember: true);
        fake.AccessToken = "rotated-by-the-server";   // the token the app holds is now stale

        var me = await client.GetAsync(ApiRoutes.Me, ContractsJsonContext.Default.CurrentUser);

        Assert.Equal("Ada", me.Name);
        Assert.Equal(1, fake.RefreshCount);
        Assert.Equal("refresh-2", tokens.Load());
        Assert.Equal(3, fake.Requests.Count(r => r.Path == ApiRoutes.Me));   // sign-in's own, the stale one, the retry
    }

    [AvaloniaFact]
    public async Task A_refused_refresh_signs_out_with_a_reason()
    {
        var fake = new FakeApi();
        var (client, session, _) = Client(fake);
        await client.SignInAsync(FakeApi.Email, FakeApi.Password, remember: false);
        var signedOut = 0;
        session.SignedOut += (_, _) => signedOut++;
        fake.AccessToken = "rotated-by-the-server";
        fake.RefuseRefresh = true;

        var error = await Assert.ThrowsAsync<ServiceException>(() => client.GetAsync(ApiRoutes.Me, ContractsJsonContext.Default.CurrentUser));

        Assert.Equal(ServiceException.SessionExpired, error.Code);
        Assert.False(session.IsSignedIn);
        Assert.NotNull(session.SignOutReason);
        Assert.Equal(1, signedOut);
    }

    [AvaloniaFact]
    public async Task No_answer_is_unreachable()
    {
        var (client, _, _) = Client(new Throwing());
        var error = await Assert.ThrowsAsync<ServiceException>(() => client.SignInAsync(FakeApi.Email, FakeApi.Password, remember: false));
        Assert.Equal(ServiceException.Unreachable, error.Code);
    }

    [AvaloniaFact]
    public async Task A_call_made_before_signing_in_waits_for_the_sign_in_and_then_goes_through()
    {
        var fake = new FakeApi();
        var (client, _, _) = Client(fake);
        var early = client.GetAsync(ApiRoutes.Me, ContractsJsonContext.Default.CurrentUser);
        await Task.Delay(50);
        Assert.False(early.IsCompleted);
        Assert.Empty(fake.Requests);

        await client.SignInAsync(FakeApi.Email, FakeApi.Password, remember: false);
        Assert.Equal("Ada", (await early).Name);
    }
}
