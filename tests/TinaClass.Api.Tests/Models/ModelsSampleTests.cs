using System.Net;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Models;
using TinaClass.Contracts.Sample;

namespace TinaClass.Api.Tests.Models;

/// <summary>The models of the sample workspace, which has its own database (the other model tests count the built-in Echo alone).</summary>
public sealed class ModelsSampleTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_Model_Switched_Off_Is_Hidden_From_Members_But_Not_From_Managers_And_The_Default_Moves()
    {
        await api.SeedSampleAsync();
        using var owner = await api.SignInAsync(SampleCore.People[0].Email);
        using var member = await api.SignInAsync(SampleCore.People[2].Email);
        var all = await owner.GetJsonAsync<IReadOnlyList<ModelDto>>(ApiRoutes.Models + "?all=true");
        var other = all.First(m => !m.IsDefault && m.Enabled && m.Kind == "chat");
        var off = await Expect.JsonAsync<ModelDto>(await owner.PatchJsonAsync($"{ApiRoutes.Models}/{other.Id}", new UpdateModelRequest(null, false, null, other.Version)));
        Assert.False(off.Enabled);
        Assert.DoesNotContain(await member.GetJsonAsync<IReadOnlyList<ModelDto>>(ApiRoutes.Models), m => m.Id == other.Id);
        Assert.Contains(await owner.GetJsonAsync<IReadOnlyList<ModelDto>>(ApiRoutes.Models + "?all=true"), m => m.Id == other.Id);
        // A member asking for "all" still only sees what they may use.
        Assert.DoesNotContain(await member.GetJsonAsync<IReadOnlyList<ModelDto>>(ApiRoutes.Models + "?all=true"), m => m.Id == other.Id);

        var chosen = all.First(m => !m.IsDefault && m.Enabled && m.Kind == "chat" && m.Id != other.Id);
        var made = await Expect.JsonAsync<ModelDto>(await owner.PatchJsonAsync($"{ApiRoutes.Models}/{chosen.Id}", new UpdateModelRequest(null, null, true, chosen.Version)));
        Assert.True(made.IsDefault);
        Assert.Single(await owner.GetJsonAsync<IReadOnlyList<ModelDto>>(ApiRoutes.Models + "?all=true"), m => m.IsDefault);
    }
}
