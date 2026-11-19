using System.Net;
using System.Net.Http.Json;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Auth;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Common;

public sealed class RateLimitTests
{
    [Fact]
    public async Task The_Auth_Endpoints_Are_Limited_Per_Address_And_Say_So_With_A_Code()
    {
        using var host = new TempHost("Production", ("Jwt:SigningKey", ApiFactory.SigningKey), ("Auth:SignInRequestsPerMinute", "3"));
        using var client = host.CreateClient();
        var login = new LoginRequest("nobody@example.com", "whatever", false);
        for (var i = 0; i < 3; i++)
        {
            await Expect.ProblemAsync(await client.PostAsJsonAsync(ApiRoutes.Auth + "/login", login, ApiFactory.Json, TestContext.Current.CancellationToken),
                HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials);
        }
        await Expect.ProblemAsync(await client.PostAsJsonAsync(ApiRoutes.Auth + "/login", login, ApiFactory.Json, TestContext.Current.CancellationToken),
            HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited);
    }
}
