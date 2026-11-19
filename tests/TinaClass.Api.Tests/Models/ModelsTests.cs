using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Models.Data;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Models;

namespace TinaClass.Api.Tests.Models;

public sealed class ModelsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(string role) => await api.SignInAsync((await api.CreateUserAsync(role)).Email);

    [Fact]
    public async Task Echo_Is_There_From_The_First_Start_And_Is_The_Default()
    {
        using var viewer = await ClientAsync(Roles.Viewer);
        var models = await viewer.GetJsonAsync<IReadOnlyList<ModelDto>>(ApiRoutes.Models);
        var echo = Assert.Single(models);
        Assert.Equal(("Echo", true, true), (echo.DisplayName, echo.IsDefault, echo.Enabled));
    }

    [Fact]
    public async Task Each_Role_Gets_The_Answer_Its_Permissions_Allow()
    {
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Models, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Providers, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);

        using var viewer = await ClientAsync(Roles.Viewer);
        using var member = await ClientAsync(Roles.Member);
        foreach (var client in new[] { viewer, member })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ApiRoutes.Models, Ct)).StatusCode);
            await Expect.ProblemAsync(await client.GetAsync(ApiRoutes.Providers, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
            await Expect.ProblemAsync(await client.PostJsonAsync(ApiRoutes.Providers, new CreateProviderRequest("X", ProviderProtocols.Local, "http://127.0.0.1:1")),
                HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        }

        using var admin = await ClientAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(ApiRoutes.Providers, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_Provider_Can_Be_Added_Changed_And_Removed_But_Echo_Cannot()
    {
        using var owner = await ClientAsync(Roles.Owner);
        var created = await Expect.JsonAsync<ProviderDto>(
            await owner.PostJsonAsync(ApiRoutes.Providers, new CreateProviderRequest("Halo", ProviderProtocols.OpenAiCompatible, "https://halo.example/v1")), HttpStatusCode.Created);
        Assert.Equal(("Halo", true, false), (created.Name, created.RequiresKey, created.KeySet));

        var renamed = await Expect.JsonAsync<ProviderDto>(await owner.PatchJsonAsync($"{ApiRoutes.Providers}/{created.Id}", new UpdateProviderRequest("Halo 2", null, null, created.Version)));
        Assert.Equal("Halo 2", renamed.Name);
        Assert.Equal(created.Version + 1, renamed.Version);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.SendDeleteAsync($"{ApiRoutes.Providers}/{created.Id}")).StatusCode);
        var providers = await owner.GetJsonAsync<IReadOnlyList<ProviderDto>>(ApiRoutes.Providers);
        Assert.DoesNotContain(providers, p => p.Id == created.Id);
        var echo = Assert.Single(providers, p => p.Protocol == ProviderProtocols.Echo);
        await Expect.ProblemAsync(await owner.SendDeleteAsync($"{ApiRoutes.Providers}/{echo.Id}"), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        await Expect.ProblemAsync(await owner.PatchJsonAsync($"{ApiRoutes.Providers}/{echo.Id}", new UpdateProviderRequest(null, null, false, echo.Version)), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
    }

    [Fact]
    public async Task A_Bad_Provider_Is_Refused_With_The_Fields_Named()
    {
        using var owner = await ClientAsync(Roles.Owner);
        var fields = await Expect.ProblemAsync(await owner.PostJsonAsync(ApiRoutes.Providers, new CreateProviderRequest("", "carrier-pigeon", "not a url")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["baseUrl", "name", "protocol"], fields.Order(StringComparer.Ordinal));
        await Expect.ProblemAsync(await owner.PostJsonAsync(ApiRoutes.Providers, new CreateProviderRequest("Echo again", ProviderProtocols.Echo, "builtin://echo")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [Fact]
    public async Task A_Stale_Version_Is_A_409_Conflict()
    {
        using var owner = await ClientAsync(Roles.Owner);
        var created = await Expect.JsonAsync<ProviderDto>(
            await owner.PostJsonAsync(ApiRoutes.Providers, new CreateProviderRequest("Race", ProviderProtocols.Local, "http://127.0.0.1:11434")), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.OK, (await owner.PatchJsonAsync($"{ApiRoutes.Providers}/{created.Id}", new UpdateProviderRequest("First", null, null, created.Version))).StatusCode);
        await Expect.ProblemAsync(await owner.PatchJsonAsync($"{ApiRoutes.Providers}/{created.Id}", new UpdateProviderRequest("Second", null, null, created.Version)), HttpStatusCode.Conflict, ErrorCodes.Conflict);
    }

    [Fact]
    public async Task A_Key_Goes_In_Encrypted_And_Never_Comes_Out()
    {
        using var owner = await ClientAsync(Roles.Owner);
        var provider = await Expect.JsonAsync<ProviderDto>(
            await owner.PostJsonAsync(ApiRoutes.Providers, new CreateProviderRequest("Keyed", ProviderProtocols.OpenAiCompatible, "https://keyed.example/v1")), HttpStatusCode.Created);
        const string key = "sk-live-super-secret-value-9f3c";

        var response = await owner.PutJsonAsync($"{ApiRoutes.Providers}/{provider.Id}/key", new SetKeyRequest(key));
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(key, raw);
        var keyed = JsonSerializer.Deserialize(raw, ContractsJsonContext.Default.ProviderDto)!;
        Assert.True(keyed.KeySet);
        Assert.EndsWith("9f3c", keyed.KeyHint);

        Assert.DoesNotContain(key, await (await owner.GetAsync(ApiRoutes.Providers, Ct)).Content.ReadAsStringAsync(Ct));
        Assert.DoesNotContain(api.Audit.Records, r => (r.ResourceName ?? "").Contains(key) || (r.ResourceId ?? "").Contains(key));
        Assert.Contains(api.Audit.Records, r => r.Action == ModelsEvents.KeyChanged && r.ResourceName == "Keyed");

        var stored = await api.InScopeAsync(async services => await services.GetRequiredService<ModelsDbContext>().Providers.AsNoTracking().Where(p => p.Id == provider.Id).Select(p => p.KeyCipher).SingleAsync(Ct));
        Assert.NotNull(stored);
        Assert.DoesNotContain(key, stored);

        var cleared = await Expect.JsonAsync<ProviderDto>(await owner.SendDeleteAsync($"{ApiRoutes.Providers}/{provider.Id}/key"));
        Assert.False(cleared.KeySet);
        Assert.Null(cleared.KeyHint);
    }

    [Fact]
    public async Task Only_The_Owner_May_Change_A_Key_And_A_Provider_That_Needs_None_Refuses_One()
    {
        using var owner = await ClientAsync(Roles.Owner);
        using var admin = await ClientAsync(Roles.Admin);
        var provider = await Expect.JsonAsync<ProviderDto>(
            await owner.PostJsonAsync(ApiRoutes.Providers, new CreateProviderRequest("Local", ProviderProtocols.Local, "http://127.0.0.1:11434")), HttpStatusCode.Created);
        await Expect.ProblemAsync(await admin.PutJsonAsync($"{ApiRoutes.Providers}/{provider.Id}/key", new SetKeyRequest("sk-x")), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await admin.SendDeleteAsync($"{ApiRoutes.Providers}/{provider.Id}/key"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await owner.PutJsonAsync($"{ApiRoutes.Providers}/{provider.Id}/key", new SetKeyRequest("sk-y")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [Fact]
    public async Task An_Unknown_Provider_Is_A_404()
    {
        using var owner = await ClientAsync(Roles.Owner);
        await Expect.ProblemAsync(await owner.PatchJsonAsync($"{ApiRoutes.Providers}/{Guid.NewGuid()}", new UpdateProviderRequest("x", null, null, 0)), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await owner.PostAsync($"{ApiRoutes.Providers}/{Guid.NewGuid()}/test", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Model_Can_Be_Renamed_And_The_Default_Cannot_Be_Switched_Off()
    {
        using var owner = await ClientAsync(Roles.Owner);
        var echo = Assert.Single(await owner.GetJsonAsync<IReadOnlyList<ModelDto>>(ApiRoutes.Models + "?all=true"), m => m.ProviderName == "TinaClass Echo");
        var renamed = await Expect.JsonAsync<ModelDto>(await owner.PatchJsonAsync($"{ApiRoutes.Models}/{echo.Id}", new UpdateModelRequest("Echo sample", null, null, echo.Version)));
        Assert.Equal("Echo sample", renamed.DisplayName);
        await Expect.ProblemAsync(await owner.PatchJsonAsync($"{ApiRoutes.Models}/{echo.Id}", new UpdateModelRequest(null, false, null, renamed.Version)), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        // The tests of this class share one database: put the name back for the ones that read it.
        var restored = await Expect.JsonAsync<ModelDto>(await owner.PatchJsonAsync($"{ApiRoutes.Models}/{echo.Id}", new UpdateModelRequest(echo.DisplayName, null, null, renamed.Version)));
        Assert.Equal(echo.DisplayName, restored.DisplayName);
    }
}
