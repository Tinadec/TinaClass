using System.Net;
using System.Text;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Common;

/// <summary>A request the API cannot read is a 400 problem with a code, in every environment (Development makes the framework throw).</summary>
public sealed class RequestErrorsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_Body_That_Is_Not_Json_Or_Is_Missing_Is_A_400_Problem(string environment)
    {
        using var host = new TempHost(environment, ("Jwt:SigningKey", ApiFactory.SigningKey), ("Seed:SampleData", "false"));
        using var client = host.CreateClient();
        using var broken = new StringContent("{not json", Encoding.UTF8, "application/json");
        await Expect.ProblemAsync(await client.PostAsync(ApiRoutes.Auth + "/login", broken, Ct), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        using var empty = new StringContent("", Encoding.UTF8, "application/json");
        await Expect.ProblemAsync(await client.PostAsync(ApiRoutes.Auth + "/login", empty, Ct), HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }
}
