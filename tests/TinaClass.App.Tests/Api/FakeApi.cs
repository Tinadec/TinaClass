using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Services;
using TinaClass.Contracts;
using TinaClass.Contracts.Auth;

namespace TinaClass.App.Tests.Api;

/// <summary>
/// A stand-in for the API (an <see cref="HttpMessageHandler"/>): sign-in with one account, refresh, sign-out, <c>/me</c> and a
/// log of the requests. The tests never touch the network.
/// </summary>
public sealed class FakeApi : HttpMessageHandler
{
    public const string Email = "ada@example.com";
    public const string Password = "right-password";

    private readonly List<(string Method, string Path, string? Authorization)> _requests = [];

    /// <summary>The access token a sign-in or refresh hands out next; a request with any other Bearer token is answered 401.</summary>
    public string AccessToken { get; set; } = "access-1";

    public int RefreshCount { get; private set; }

    /// <summary>When set, the refresh endpoint refuses every refresh token (the session was ended on the server).</summary>
    public bool RefuseRefresh { get; set; }

    /// <summary>When set, every request is answered with this status and a problem body with this code.</summary>
    public (HttpStatusCode Status, string Code)? Failure { get; set; }

    public IReadOnlyList<(string Method, string Path, string? Authorization)> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        lock (_requests)
        {
            _requests.Add((request.Method.Method, path, request.Headers.Authorization?.Parameter));
        }
        return Task.FromResult(Answer(request, path));
    }

    private HttpResponseMessage Answer(HttpRequestMessage request, string path)
    {
        if (Failure is { } failure)
        {
            return Problem(failure.Status, failure.Code);
        }
        var method = request.Method.Method;
        var body = request.Content is null ? "" : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        switch (method, path)
        {
            case ("POST", "/api/v1/auth/login"):
                var login = JsonSerializer.Deserialize(body, ContractsJsonContext.Default.LoginRequest)!;
                return login.Email == Email && login.Password == Password ? Json(HttpStatusCode.OK, Tokens("refresh-1")) : Problem(HttpStatusCode.Unauthorized, "invalid_credentials");
            case ("POST", "/api/v1/auth/refresh") when RefuseRefresh:
                return Problem(HttpStatusCode.Unauthorized, "invalid_refresh_token");
            case ("POST", "/api/v1/auth/refresh"):
                RefreshCount++;
                AccessToken = $"access-{RefreshCount + 1}";
                return Json(HttpStatusCode.OK, Tokens($"refresh-{RefreshCount + 1}"));
            case ("POST", "/api/v1/auth/logout"):
                return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        if (request.Headers.Authorization?.Parameter != AccessToken)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
        switch (method, path)
        {
            case ("GET", "/api/v1/me"):
                return Json(HttpStatusCode.OK, $$"""{"id":"{{Guid.Empty}}","email":"{{Email}}","name":"Ada","roleId":"member","permissions":["chat.use"]}""");
        }
        return Problem(HttpStatusCode.NotFound, "not_found");
    }

    private string Tokens(string refresh) =>
        $$"""{"accessToken":"{{AccessToken}}","accessTokenExpiresAt":"2030-01-01T00:00:00Z","refreshToken":"{{refresh}}"}""";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code) =>
        new(status) { Content = new StringContent($$"""{"status":{{(int)status}},"title":"{{code}}","code":"{{code}}"}""", Encoding.UTF8, "application/problem+json") };

    /// <summary>
    /// The app as the tests see it with <c>"dataSource": "api"</c> and this fake as the server (tokens kept in memory). Dispose it to
    /// give the next test the sample data again.
    /// </summary>
    public IDisposable UseApiMode()
    {
        TestApp.Prepare();
        ApiSettings.Current = new ApiSettings { DataSource = "api", ApiBaseUrl = "http://api.test" };
        DesktopApp.Services = AppServices.Build(services =>
        {
            services.AddSingleton<TokenStore>(new MemoryTokenStore());
            services.AddSingleton(sp => new ApiClient(sp.GetRequiredService<ApiSession>(), sp.GetRequiredService<TokenStore>(), this));
        });
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => ApiSettings.Current = new ApiSettings();
    }
}
