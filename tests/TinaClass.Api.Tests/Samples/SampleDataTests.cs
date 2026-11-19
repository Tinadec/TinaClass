using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Models;
using TinaClass.Contracts.Sample;
using TinaClass.Contracts.Knowledge;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Prompts;
using TinaClass.Contracts.Usage;
using TinaClass.Contracts.Workflows;
using TinaClass.Contracts.Team;

namespace TinaClass.Api.Tests.Samples;

/// <summary>The sample workspace (design 16) is what the previews, the README and the app's offline mode show: every seeder runs, in order, once, and the numbers match.</summary>
public sealed class SampleDataTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> OwnerAsync()
    {
        await api.SeedSampleAsync();
        return await api.SignInAsync(SampleCore.People[0].Email);
    }

    [Fact]
    public async Task Seeding_Twice_Changes_Nothing()
    {
        using var owner = await OwnerAsync();
        var before = (await owner.GetJsonAsync<Paged<ConversationDto>>(ApiRoutes.Conversations + "?all=true&pageSize=100")).Total;
        await api.SeedSampleAsync();
        Assert.Equal(before, (await owner.GetJsonAsync<Paged<ConversationDto>>(ApiRoutes.Conversations + "?all=true&pageSize=100")).Total);
        Assert.True(before > 0);
    }

    [Fact]
    public async Task The_Sample_Providers_Hold_Keys_Only_As_A_Hint()
    {
        using var owner = await OwnerAsync();
        var providers = await owner.GetJsonAsync<IReadOnlyList<ProviderDto>>(ApiRoutes.Providers);
        Assert.True(providers.Count >= 5);
        var northlight = providers.Single(p => p.Name == "Northlight Cloud");
        Assert.True(northlight.KeySet);
        Assert.EndsWith("4f2a", northlight.KeyHint);
        Assert.DoesNotContain("sk-", northlight.KeyHint);
        Assert.Contains(providers, p => p is { Name: "Meridian", Status: "no_key", KeySet: false });
        var models = await owner.GetJsonAsync<IReadOnlyList<ModelDto>>(ApiRoutes.Models + "?all=true");
        Assert.Single(models, m => m.IsDefault);
    }

    [Fact]
    public async Task The_Sample_Knowledge_Bases_Total_The_Design_Counts()
    {
        using var owner = await OwnerAsync();
        var bases = await owner.GetJsonAsync<IReadOnlyList<KnowledgeBaseDto>>(ApiRoutes.Knowledge);
        Assert.Equal(4, bases.Count);
        Assert.Equal(KnowledgeSample.Bases.Sum(b => b.Documents), bases.Sum(b => b.DocumentCount));
        Assert.Equal(KnowledgeSample.Bases.Sum(b => b.Chunks), bases.Sum(b => b.ChunkCount));
    }

    [Fact]
    public async Task The_Review_Prompt_Has_Three_Versions_And_Passing_Cases()
    {
        using var owner = await OwnerAsync();
        var prompts = await owner.GetJsonAsync<IReadOnlyList<PromptDto>>(ApiRoutes.Prompts);
        Assert.Equal(7, prompts.Count);
        var review = prompts.Single(p => p.Command == "/review");
        Assert.Equal(7, review.CurrentVersion);
        Assert.Equal((3, 3), (review.CasesPassed, review.CasesTotal));
        Assert.Equal(3, (await owner.GetJsonAsync<IReadOnlyList<PromptVersionDto>>($"{ApiRoutes.Prompts}/{review.Id}/versions")).Count);
    }

    [Fact]
    public async Task One_Run_Waits_For_An_Approval()
    {
        using var owner = await OwnerAsync();
        var agents = await owner.GetJsonAsync<IReadOnlyList<AgentDto>>(ApiRoutes.Agents);
        Assert.Equal(6, agents.Count);
        var approvals = await owner.GetJsonAsync<IReadOnlyList<ApprovalDto>>(ApiRoutes.Approvals + "?state=pending");
        // The agent run's ticket and, with the workflows feature, the flow's "send" confirmation.
        var pending = Assert.Single(approvals, a => a.Id == SampleIds.Approval(1));
        Assert.All(approvals, a => Assert.Equal("pending", a.State));
        Assert.Equal(RunStates.WaitingApproval, (await owner.GetJsonAsync<RunDto>($"{ApiRoutes.Runs}/{pending.RunId}")).State);
    }

    [Fact]
    public async Task The_Month_Shows_Spend_Against_The_Budget()
    {
        using var owner = await OwnerAsync();
        var budget = await owner.GetJsonAsync<BudgetDto>(ApiRoutes.Usage + "/budget");
        Assert.Equal(200_000, budget.Amount);
        Assert.True(budget.Spent > 0);
        var summary = await owner.GetJsonAsync<UsageSummaryDto>(ApiRoutes.Usage + "/summary");
        Assert.Equal(summary.Days.Sum(d => d.Cost), summary.Total);
    }

    [Fact]
    public async Task The_Sample_Flow_Is_Published_At_Version_12_With_A_Run_Waiting_For_Confirmation()
    {
        using var owner = await OwnerAsync();
        var flow = Assert.Single(await owner.GetJsonAsync<IReadOnlyList<FlowDto>>(ApiRoutes.Flows));
        Assert.Equal(12, flow.PublishedVersion);
        Assert.Equal(12, (await owner.GetJsonAsync<IReadOnlyList<FlowVersionDto>>($"{ApiRoutes.Flows}/{flow.Id}/versions")).Count);
        var run = Assert.Single(await owner.GetJsonAsync<IReadOnlyList<FlowRunDto>>($"{ApiRoutes.Flows}/{flow.Id}/runs"));
        Assert.Equal((842, "waiting"), (run.Ordinal, run.State));
        Assert.Contains(run.Nodes, n => n.State == NodeStates.Waiting);
    }

    [Fact]
    public async Task The_Team_Page_Lists_Five_People_One_Invitation_And_The_Audit_Trail()
    {
        using var owner = await OwnerAsync();
        var members = await owner.GetJsonAsync<IReadOnlyList<MemberDto>>(ApiRoutes.Members);
        Assert.Equal(5, members.Count(m => m.Status == "active"));
        Assert.Equal(TeamSample.InvitedEmail, Assert.Single(members, m => m.Status == "invited").Email);
        var audit = await owner.GetJsonAsync<Paged<AuditEntryDto>>(ApiRoutes.Audit);
        // Every feature's audit records are stored here, so the sign-ins of this test class are in it too.
        Assert.True(audit.Total >= TeamSample.Audit.Count);
        Assert.Contains(audit.Items, a => a.Action == "provider.key_changed" && a.ActorName == SampleCore.People[0].Name);
    }

    [Fact]
    public async Task The_Sample_People_Can_Sign_In_And_Have_Their_Roles()
    {
        await api.SeedSampleAsync();
        foreach (var person in SampleCore.People)
        {
            using var client = await api.SignInAsync(person.Email);
            var me = await client.GetJsonAsync<Contracts.Auth.CurrentUser>(ApiRoutes.Me);
            Assert.Equal(person.Role, me.RoleId);
        }
        Assert.True(Ct is { });
    }
}
