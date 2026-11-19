using System.Net;
using System.Net.Http.Json;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Auth;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Common;

/// <summary>What the API refuses to start with: the rules that keep a deployment from running on public secrets.</summary>
public sealed class StartupTests
{
    private static void Start(TempHost host)
    {
        using (host)
        {
            _ = host.Server;
        }
    }

    [Fact]
    public void Outside_Development_A_Missing_Signing_Key_Stops_The_Start()
    {
        var error = Assert.ThrowsAny<Exception>(() => Start(new TempHost("Production")));
        Assert.Contains("Jwt:SigningKey", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_Development_A_Short_Signing_Key_Stops_The_Start()
    {
        var error = Assert.ThrowsAny<Exception>(() => Start(new TempHost("Production", ("Jwt:SigningKey", "too-short"))));
        Assert.Contains("at least 32", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void Sample_Data_Outside_Development_Stops_The_Start()
    {
        var error = Assert.ThrowsAny<Exception>(() => Start(new TempHost("Production",
            ("Jwt:SigningKey", ApiFactory.SigningKey), ("Seed:SampleData", "true"), ("Seed:Password", "a-public-password"))));
        Assert.Contains("Seed:SampleData", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_With_A_Good_Key_Starts()
    {
        Start(new TempHost("Production", ("Jwt:SigningKey", ApiFactory.SigningKey)));
    }

    [Fact]
    public async Task Development_Makes_Up_A_Key_And_Seeds_The_Sample_Users_With_The_Development_Password()
    {
        using var host = new TempHost("Development");
        using var client = host.CreateClient();
        // appsettings.Development.json turns the sample data on; its password is the one in that file.
        var response = await client.PostAsJsonAsync(ApiRoutes.Auth + "/login", new LoginRequest("lu.jiahe@qiyun.example", "local-dev-only-change-me", false),
            ApiFactory.Json, TestContext.Current.CancellationToken);
        var pair = await Expect.JsonAsync<TokenPair>(response);
        using var authorized = host.CreateClient();
        authorized.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pair.AccessToken);
        var me = await authorized.GetAsync(ApiRoutes.Me, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Without_The_Sample_Data_Setting_Nobody_Exists()
    {
        using var host = new TempHost("Development", ("Seed:SampleData", "false"));
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync(ApiRoutes.Auth + "/login", new LoginRequest("lu.jiahe@qiyun.example", "local-dev-only-change-me", false),
            ApiFactory.Json, TestContext.Current.CancellationToken);
        await Expect.ProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
    }

    /// <summary>The messages of an exception and everything inside it (the host wraps a start-up failure).</summary>
    private static string Flatten(Exception exception)
    {
        var parts = new List<string>();
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            parts.Add(e.Message);
        }
        return string.Join(" | ", parts);
    }
}
