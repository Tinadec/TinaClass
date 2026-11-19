using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Agents;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Prompts;
using TinaClass.Contracts.Usage;

namespace TinaClass.Api.Tests.Usage;

internal static class UsageHelpers
{
    /// <summary>Records a request that cost <paramref name="cost"/> minor units (the echo model is free, so tests write the cost themselves).</summary>
    public static async Task SpendAsync(this ApiFactory api, Guid? userId, long cost, int tokens = 1500)
    {
        await api.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IUsageRecorder>().RecordAsync(new UsageEntry(userId, null, null, "Test model", "chat", Guid.NewGuid(), tokens - 500, 500, 0, 300, 1_000, cost, "{}"), TestContext.Current.CancellationToken);
            return 0;
        });
    }

    public static async Task<BudgetDto> SetBudgetAsync(this HttpClient admin, long amount, string action, long personal = 0)
    {
        var current = await admin.GetJsonAsync<BudgetDto>(ApiRoutes.Usage + "/budget");
        return await Expect.JsonAsync<BudgetDto>(await admin.PutJsonAsync(ApiRoutes.Usage + "/budget", new UpdateBudgetRequest(amount, action, personal, current.Version)));
    }
}

public sealed class UsageTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(string role) => await api.SignInAsync((await api.CreateUserAsync(role)).Email);

    private DateOnly Today => DateOnly.FromDateTime(api.Time.GetUtcNow().UtcDateTime);

    [Fact]
    public async Task Each_Role_Gets_The_Answer_Its_Permissions_Allow()
    {
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Usage + "/summary", Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        using var viewer = await ClientAsync(Roles.Viewer);
        using var member = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        foreach (var path in new[] { "/summary", "/records", "/budget" })
        {
            await Expect.ProblemAsync(await viewer.GetAsync(ApiRoutes.Usage + path, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
            Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(ApiRoutes.Usage + path, Ct)).StatusCode);
        }
        var body = new UpdateBudgetRequest(1000, BudgetActions.Notify, 0, 0);
        await Expect.ProblemAsync(await viewer.PutJsonAsync(ApiRoutes.Usage + "/budget", body), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await member.PutJsonAsync(ApiRoutes.Usage + "/budget", body), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(1000L, (await admin.SetBudgetAsync(1000, BudgetActions.Notify)).Amount);
    }

    [Fact]
    public async Task Someone_With_Only_Read_Access_Sees_Their_Own_Use_And_An_Administrator_Sees_All()
    {
        var alice = await api.CreateUserAsync(Roles.Member, name: "Alice");
        var bob = await api.CreateUserAsync(Roles.Member, name: "Bob");
        using var asAlice = await api.SignInAsync(alice.Email);
        using var asAdmin = await ClientAsync(Roles.Admin);
        await api.SpendAsync(alice.Id, 300, 2_000);
        await api.SpendAsync(bob.Id, 700, 4_000);
        await api.SpendAsync(null, 50, 1_000);

        var mine = await asAlice.GetJsonAsync<UsageSummaryDto>(ApiRoutes.Usage + "/summary");
        Assert.Equal((300L, 1, 2_000L), (mine.Total, mine.Requests, mine.Tokens));
        var member = Assert.Single(mine.Members);
        Assert.Equal((alice.Id, "Alice", 300L), (member.UserId, member.Name, member.Cost));
        var all = await asAdmin.GetJsonAsync<UsageSummaryDto>(ApiRoutes.Usage + "/summary");
        Assert.True(all.Total >= 1_050);
        Assert.Contains(all.Members, m => m.UserId == bob.Id && m.Name == "Bob" && m.Cost == 700);
        Assert.Contains(all.Members, m => m.UserId is null && m.Name == "Agents");
        Assert.Equal(all.Total, all.Models.Sum(m => m.Cost));
        Assert.Equal(all.Total, all.Members.Sum(m => m.Cost));

        var records = await asAlice.GetJsonAsync<Paged<UsageRecordDto>>(ApiRoutes.Usage + "/records");
        Assert.Equal(alice.Id, Assert.Single(records.Items).UserId);
        Assert.Contains((await asAdmin.GetJsonAsync<Paged<UsageRecordDto>>(ApiRoutes.Usage + "/records?pageSize=100")).Items, r => r.UserId == bob.Id && r.UserName == "Bob");
    }

    [Fact]
    public async Task The_Summary_Covers_The_Month_So_Far_Or_The_Range_Asked_For_And_Refuses_A_Bad_One()
    {
        using var admin = await ClientAsync(Roles.Admin);
        await api.SpendAsync(null, 10);
        var month = await admin.GetJsonAsync<UsageSummaryDto>(ApiRoutes.Usage + "/summary");
        Assert.Equal(new DateOnly(Today.Year, Today.Month, 1), month.From);
        Assert.Equal(Today, month.To);
        Assert.Equal(Today.Day, month.Days.Count);
        Assert.Equal(month.Total, month.Days.Sum(d => d.Cost));
        Assert.Equal(Today, month.Days[^1].Day);

        var week = await admin.GetJsonAsync<UsageSummaryDto>($"{ApiRoutes.Usage}/summary?from={Today.AddDays(-6):yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        Assert.Equal(7, week.Days.Count);
        Assert.All(week.Days.Take(6), d => Assert.Equal(0, d.Requests));
        var fields = await Expect.ProblemAsync(await admin.GetAsync($"{ApiRoutes.Usage}/summary?from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}", Ct), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["from"], fields);
        await Expect.ProblemAsync(await admin.GetAsync($"{ApiRoutes.Usage}/summary?from={Today.AddDays(-400):yyyy-MM-dd}&to={Today:yyyy-MM-dd}", Ct), HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [Fact]
    public async Task The_Detail_List_Is_Paged_Newest_First_And_Filtered_By_Day()
    {
        using var admin = await ClientAsync(Roles.Admin);
        for (var i = 0; i < 3; i++)
        {
            await api.SpendAsync(null, 1 + i);
        }
        var page = await admin.GetJsonAsync<Paged<UsageRecordDto>>(ApiRoutes.Usage + "/records?page=1&pageSize=2");
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.Total >= 3);
        Assert.True(page.Items[0].At >= page.Items[1].At);
        Assert.Equal(0, (await admin.GetJsonAsync<Paged<UsageRecordDto>>($"{ApiRoutes.Usage}/records?from={Today.AddDays(2):yyyy-MM-dd}")).Total);
        Assert.Equal(page.Total, (await admin.GetJsonAsync<Paged<UsageRecordDto>>($"{ApiRoutes.Usage}/records?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}")).Total);
    }

    [Fact]
    public async Task The_Budget_Is_Checked_Versioned_And_Audited()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var fields = await Expect.ProblemAsync(await admin.PutJsonAsync(ApiRoutes.Usage + "/budget", new UpdateBudgetRequest(-1, "shrug", -5, 0)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["action", "amount", "personalAmount"], fields.Order(StringComparer.Ordinal));
        var first = await admin.SetBudgetAsync(200_000, BudgetActions.Notify, 30_000);
        Assert.Equal((200_000L, BudgetActions.Notify, 30_000L), (first.Amount, first.Action, first.PersonalAmount));
        Assert.Equal([50, 80], first.WarnAtPercents);
        var stale = new UpdateBudgetRequest(300_000, BudgetActions.Block, 0, first.Version - 1);
        await Expect.ProblemAsync(await admin.PutJsonAsync(ApiRoutes.Usage + "/budget", stale), HttpStatusCode.Conflict, ErrorCodes.Conflict);
        var second = await admin.SetBudgetAsync(300_000, BudgetActions.Notify);
        Assert.True(second.Version > first.Version);
        Assert.Equal(300_000L, (await admin.GetJsonAsync<BudgetDto>(ApiRoutes.Usage + "/budget")).Amount);
        Assert.Contains(api.Audit.Records, r => r.Action == "usage.budget_updated");
    }
}

public sealed class UsageBudgetTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(string role) => await api.SignInAsync((await api.CreateUserAsync(role)).Email);

    private async Task<HttpResponseMessage> SendChatAsync(HttpClient client)
    {
        var chat = await Expect.JsonAsync<ConversationDto>(await client.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest(null, null, null, null)), HttpStatusCode.Created);
        return await client.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/messages", new SendMessageRequest("hello", null, null, null, false, null, null));
    }

    private async Task<RunDto> StartAgentAsync(HttpClient client)
    {
        var agent = await Expect.JsonAsync<AgentDto>(await client.PostJsonAsync(ApiRoutes.Agents, new SaveAgentRequest("Budget agent", "", "Help.", "", 5, 0, "workspace", null, null, [], [], 0)), HttpStatusCode.Created);
        await Expect.JsonAsync<AgentDto>(await client.PostAsync($"{ApiRoutes.Agents}/{agent.Id}/publish", null, Ct));
        var run = await Expect.JsonAsync<RunDto>(await client.PostJsonAsync($"{ApiRoutes.Agents}/{agent.Id}/runs", new StartRunRequest("go")), HttpStatusCode.Accepted);
        await api.Services.GetRequiredService<RunRegistry>().WhenIdleAsync(run.Id).WaitAsync(TimeSpan.FromSeconds(20), Ct);
        return await client.GetJsonAsync<RunDto>($"{ApiRoutes.Runs}/{run.Id}");
    }

    [Fact]
    public async Task A_Spent_Workspace_Budget_That_Blocks_Stops_Chat_Prompts_And_Agents_And_Notify_Stops_Nothing()
    {
        using var admin = await ClientAsync(Roles.Admin);
        using var member = await ClientAsync(Roles.Member);
        var prompt = await Expect.JsonAsync<PromptDto>(await member.PostJsonAsync(ApiRoutes.Prompts, new SavePromptRequest("Budget prompt", "/b" + Guid.NewGuid().ToString("N")[..8], "workspace", "", "Say {{x}}", 0)), HttpStatusCode.Created);
        var run = new RunPromptRequest(new Dictionary<string, string> { ["x"] = "hi" }, null);
        await api.SpendAsync(null, 500);

        await admin.SetBudgetAsync(400, BudgetActions.Notify);
        Assert.Equal(HttpStatusCode.OK, (await SendChatAsync(member)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/run", run)).StatusCode);

        await admin.SetBudgetAsync(400, BudgetActions.Block);
        await Expect.ProblemAsync(await SendChatAsync(member), HttpStatusCode.PaymentRequired, ErrorCodes.BudgetExceeded);
        await Expect.ProblemAsync(await member.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/run", run), HttpStatusCode.PaymentRequired, ErrorCodes.BudgetExceeded);
        Assert.Equal(RunStates.Failed, (await StartAgentAsync(member)).State);

        // More money, and everything works again.
        await admin.SetBudgetAsync(100_000, BudgetActions.Block);
        Assert.Equal(HttpStatusCode.OK, (await SendChatAsync(member)).StatusCode);
        Assert.Equal(RunStates.Succeeded, (await StartAgentAsync(member)).State);
        await admin.SetBudgetAsync(0, BudgetActions.Notify);
    }

    [Fact]
    public async Task A_Personal_Budget_Blocks_Only_The_Person_Who_Spent_It()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var spender = await api.CreateUserAsync(Roles.Member);
        using var asSpender = await api.SignInAsync(spender.Email);
        using var other = await ClientAsync(Roles.Member);
        await api.SpendAsync(spender.Id, 200);

        await admin.SetBudgetAsync(0, BudgetActions.Block, personal: 150);
        await Expect.ProblemAsync(await SendChatAsync(asSpender), HttpStatusCode.PaymentRequired, ErrorCodes.BudgetExceeded);
        Assert.Equal(HttpStatusCode.OK, (await SendChatAsync(other)).StatusCode);
        await admin.SetBudgetAsync(0, BudgetActions.Notify);
        Assert.Equal(HttpStatusCode.OK, (await SendChatAsync(asSpender)).StatusCode);
    }
}

public sealed class UsageThresholdTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private int Crossings => api.Events.Events.Count(e => e.Type == UsageEvents.ThresholdCrossed);

    [Fact]
    public async Task Each_Threshold_Is_Announced_Once_A_Month_And_A_New_Amount_Starts_Over()
    {
        using var admin = await api.SignInAsync((await api.CreateUserAsync(Roles.Admin)).Email);
        await admin.SetBudgetAsync(1_000, BudgetActions.Notify);
        var before = Crossings;

        await api.SpendAsync(null, 400);
        Assert.Equal(before, Crossings);
        await api.SpendAsync(null, 150);
        Assert.Equal(before + 1, Crossings);
        await api.SpendAsync(null, 100);
        Assert.Equal(before + 1, Crossings);
        await api.SpendAsync(null, 200);
        Assert.Equal(before + 2, Crossings);
        await api.SpendAsync(null, 200);
        await api.SpendAsync(null, 100);
        Assert.Equal(before + 3, Crossings);
        Assert.Equal(BudgetStates.Over, (await admin.GetJsonAsync<BudgetDto>(ApiRoutes.Usage + "/budget")).State);
        Assert.Contains(api.Events.Events, e => e.Type == UsageEvents.ThresholdCrossed && e.Json.Contains("\"state\":\"over\""));

        // A new amount starts the month's alerts again.
        await admin.SetBudgetAsync(1_150, BudgetActions.Notify);
        await api.SpendAsync(null, 1);
        Assert.Equal(before + 4, Crossings);
    }
}
