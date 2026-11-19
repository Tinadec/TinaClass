using System.Net;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Agents;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Agents;

public sealed class AgentRunTests(ApiFactory api) : AgentsTestBase(api)
{
    [Fact]
    public async Task A_Run_Stops_At_Each_Tool_That_Needs_A_Person_And_Only_Runs_What_Was_Approved()
    {
        using var member = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var read = await SeedToolAsync("read");
        var write = await SeedToolAsync("write");
        var send = await SeedToolAsync("external");
        var agent = await NewAgentAsync(member, [Use(read), Use(write, ToolApprovals.Ask), Use(send)]);

        var run = await RunAsync(member, agent);
        Assert.Equal(RunStates.WaitingApproval, run.State);
        Assert.Equal([read.Name], Spy.Calls);
        Assert.Equal(["think", "tool", "approval"], run.Steps.Select(s => s.Kind));
        Assert.Equal("waiting", run.Steps[^1].State);
        var first = Assert.Single(await PendingAsync(admin, run.Id));
        Assert.Equal((write.Name, ToolRisks.Write, "agent"), (first.Title, first.Risk, first.Source));

        var approved = await Expect.JsonAsync<ApprovalDto>(await DecideAsync(admin, first.Id, true, "Fine."));
        Assert.Equal(("approved", "Fine."), (approved.State, approved.Reason));
        run = await IdleAsync(member, run.Id);
        Assert.Equal(RunStates.WaitingApproval, run.State);
        Assert.Equal([read.Name, write.Name], Spy.Calls);

        // An external tool is asked for even though the agent says "auto".
        var second = Assert.Single(await PendingAsync(admin, run.Id));
        Assert.Equal(send.Name, second.Title);
        await Expect.JsonAsync<ApprovalDto>(await DecideAsync(admin, second.Id, false, "Not to this customer."));
        run = await IdleAsync(member, run.Id);
        Assert.Equal(RunStates.Succeeded, run.State);
        Assert.DoesNotContain(send.Name, Spy.Calls);
        Assert.Contains(run.Steps, s => s.Title == $"Not using {send.Name}" && s.Detail.Contains("Not to this customer."));
        Assert.Equal("reply", run.Steps[^1].Kind);
        Assert.NotNull(run.EndedAt);

        await Expect.ProblemAsync(await DecideAsync(admin, first.Id, true), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        await Expect.ProblemAsync(await DecideAsync(admin, second.Id, true), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        Assert.Equal(2, Api.Events.Events.Count(e => e.Type == AgentsEvents.RunWaitingApproval && e.Json.Contains(run.Id.ToString())));
        Assert.Single(Api.Events.Events, e => e.Type == AgentsEvents.RunCompleted && e.Json.Contains(run.Id.ToString()));
        Assert.Contains(Api.Audit.Records, r => r.Action == AgentsAuditActions.ApprovalApproved && r.ResourceId == first.Id.ToString());
        Assert.Contains(Api.Audit.Records, r => r.Action == AgentsAuditActions.ApprovalRejected && r.ResourceId == second.Id.ToString());
    }

    [Fact]
    public async Task Nobody_Approves_What_They_Started_Except_The_Owner()
    {
        using var admin = await ClientAsync(Roles.Admin);
        using var other = await ClientAsync(Roles.Admin);
        using var owner = await ClientAsync(Roles.Owner);
        var write = await SeedToolAsync("write");
        var agent = await NewAgentAsync(admin, [Use(write, ToolApprovals.Ask)]);

        var run = await RunAsync(admin, agent);
        var ticket = Assert.Single(await PendingAsync(admin, run.Id));
        await Expect.ProblemAsync(await DecideAsync(admin, ticket.Id, true), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await DecideAsync(admin, ticket.Id, false), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.DoesNotContain(write.Name, Spy.Calls);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(other, ticket.Id, true)).StatusCode);
        Assert.Equal(RunStates.Succeeded, (await IdleAsync(admin, run.Id)).State);

        var ownRun = await RunAsync(owner, agent);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(owner, Assert.Single(await PendingAsync(owner, ownRun.Id)).Id, true)).StatusCode);
        Assert.Equal(RunStates.Succeeded, (await IdleAsync(owner, ownRun.Id)).State);
    }

    [Fact]
    public async Task Cancelling_A_Waiting_Run_Rejects_Its_Approval_And_Never_Runs_The_Tool()
    {
        using var starter = await ClientAsync(Roles.Member);
        using var stranger = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var write = await SeedToolAsync("write");
        var agent = await NewAgentAsync(admin, [Use(write, ToolApprovals.Ask)]);
        var run = await RunAsync(starter, agent);
        Assert.Equal(RunStates.WaitingApproval, run.State);
        var ticket = Assert.Single(await PendingAsync(admin, run.Id));

        await Expect.ProblemAsync(await stranger.PostAsync($"{ApiRoutes.Runs}/{run.Id}/cancel", null, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        var cancelled = await Expect.JsonAsync<RunDto>(await starter.PostAsync($"{ApiRoutes.Runs}/{run.Id}/cancel", null, Ct));
        Assert.Equal(RunStates.Cancelled, cancelled.State);
        Assert.NotNull(cancelled.EndedAt);
        Assert.Equal("cancelled", cancelled.Steps[^1].State);
        Assert.Equal("rejected", (await admin.GetJsonAsync<IReadOnlyList<ApprovalDto>>(ApiRoutes.Approvals)).Single(a => a.Id == ticket.Id).State);
        await Expect.ProblemAsync(await DecideAsync(admin, ticket.Id, true), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        await Expect.ProblemAsync(await starter.PostAsync($"{ApiRoutes.Runs}/{run.Id}/cancel", null, Ct), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        Assert.DoesNotContain(write.Name, Spy.Calls);
        // The agent's owner (and an approver) may cancel too.
        var second = await RunAsync(starter, agent);
        Assert.Equal(RunStates.Cancelled, (await Expect.JsonAsync<RunDto>(await admin.PostAsync($"{ApiRoutes.Runs}/{second.Id}/cancel", null, Ct))).State);
    }

    [Fact]
    public async Task A_Write_Tool_That_The_Agent_Does_Not_Ask_About_Runs_And_A_Disabled_One_Is_Skipped()
    {
        using var member = await ClientAsync(Roles.Member);
        var write = await SeedToolAsync("write");
        var off = await SeedToolAsync("read");
        var agent = await NewAgentAsync(member, [Use(write), Use(off, enabled: false)]);
        var run = await RunAsync(member, agent);
        Assert.Equal(RunStates.Succeeded, run.State);
        Assert.Contains(write.Name, Spy.Calls);
        Assert.DoesNotContain(off.Name, Spy.Calls);
        Assert.Contains(run.Steps, s => s.Title == $"Call {write.Name}" && s.State == "done" && s.Detail.StartsWith("spy", StringComparison.Ordinal));
        Assert.Equal(1, (await member.GetJsonAsync<AgentDto>($"{ApiRoutes.Agents}/{agent.Id}")).Runs);
    }

    [Fact]
    public async Task A_Run_That_Reaches_The_Step_Limit_Fails()
    {
        using var member = await ClientAsync(Roles.Member);
        var tools = new List<TinaClass.Api.Features.Agents.Data.Tool>();
        for (var i = 0; i < 3; i++)
        {
            tools.Add(await SeedToolAsync("read"));
        }
        var agent = await NewAgentAsync(member, [.. tools.Select(t => Use(t))], maxSteps: 2);
        var run = await RunAsync(member, agent);
        Assert.Equal(RunStates.Failed, run.State);
        Assert.Equal([tools[0].Name], Spy.Calls.Where(c => tools.Any(t => t.Name == c)));
        Assert.Equal(0, (await member.GetJsonAsync<AgentDto>($"{ApiRoutes.Agents}/{agent.Id}")).SuccessPercent);
    }

    [Fact]
    public async Task Starting_A_Run_Checks_The_Subject_And_Counts_A_Success()
    {
        using var member = await ClientAsync(Roles.Member);
        var agent = await NewAgentAsync(member);
        var fields = await Expect.ProblemAsync(await member.PostJsonAsync($"{ApiRoutes.Agents}/{agent.Id}/runs", new StartRunRequest(" ")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["subject"], fields);
        await Expect.ProblemAsync(await member.PostJsonAsync($"{ApiRoutes.Agents}/{Guid.NewGuid()}/runs", new StartRunRequest("go")), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        var run = await RunAsync(member, agent, "Summarize the week");
        Assert.Equal(RunStates.Succeeded, run.State);
        Assert.Equal("manual", run.Trigger);
        Assert.True(run.Tokens > 0);
        Assert.Contains("Summarize the week", run.Steps[^1].Detail);
        var stored = await member.GetJsonAsync<AgentDto>($"{ApiRoutes.Agents}/{agent.Id}");
        Assert.Equal((1, 100), (stored.Runs, stored.SuccessPercent));
        Assert.Null(stored.LiveState);
    }

    [Fact]
    public async Task Runs_Are_Listed_By_Agent_And_State_And_In_Pages()
    {
        using var member = await ClientAsync(Roles.Member);
        var write = await SeedToolAsync("write");
        var plain = await NewAgentAsync(member);
        var asking = await NewAgentAsync(member, [Use(write, ToolApprovals.Ask)]);
        await RunAsync(member, plain);
        await RunAsync(member, plain);
        var waiting = await RunAsync(member, asking);

        var mine = await member.GetJsonAsync<Paged<RunDto>>($"{ApiRoutes.Runs}?agentId={plain.Id}");
        Assert.Equal(2, mine.Total);
        Assert.All(mine.Items, r => Assert.Equal(plain.Id, r.AgentId));
        var one = await member.GetJsonAsync<Paged<RunDto>>($"{ApiRoutes.Runs}?agentId={plain.Id}&page=2&pageSize=1");
        Assert.Equal((2, 1, 2), (one.Total, one.Items.Count, one.Page));
        var open = await member.GetJsonAsync<Paged<RunDto>>($"{ApiRoutes.Runs}?state={RunStates.WaitingApproval}&agentId={asking.Id}");
        Assert.Equal(waiting.Id, Assert.Single(open.Items).Id);
        Assert.Equal(RunStates.WaitingApproval, (await member.GetJsonAsync<AgentDto>($"{ApiRoutes.Agents}/{asking.Id}")).LiveState);
    }

    [Fact]
    public async Task The_Events_Of_A_Run_End_With_Its_Last_State_And_A_Private_Agent_Hides_Its_Runs()
    {
        using var alice = await ClientAsync(Roles.Member);
        using var bob = await ClientAsync(Roles.Member);
        var shared = await RunAsync(alice, await NewAgentAsync(alice));
        var stream = await alice.GetAsync($"{ApiRoutes.Runs}/{shared.Id}/events", Ct);
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        var text = await stream.Content.ReadAsStringAsync(Ct);
        Assert.Contains("event: step", text);
        Assert.Contains("event: run", text);
        Assert.Contains("\"state\":\"succeeded\"", text);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"{ApiRoutes.Runs}/{shared.Id}", Ct)).StatusCode);

        var secret = await RunAsync(alice, await NewAgentAsync(alice, visibility: "private"));
        foreach (var response in new[] { await bob.GetAsync($"{ApiRoutes.Runs}/{secret.Id}", Ct), await bob.GetAsync($"{ApiRoutes.Runs}/{secret.Id}/events", Ct), await bob.PostAsync($"{ApiRoutes.Runs}/{secret.Id}/cancel", null, Ct) })
        {
            await Expect.ProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        }
        Assert.DoesNotContain((await bob.GetJsonAsync<Paged<RunDto>>(ApiRoutes.Runs)).Items, r => r.Id == secret.Id);
        await Expect.ProblemAsync(await alice.GetAsync($"{ApiRoutes.Runs}/{Guid.NewGuid()}/events", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Tool_Asked_For_In_Chat_Is_Never_Run_And_Becomes_An_Approval()
    {
        using var member = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var read = await SeedToolAsync("read");
        var send = await SeedToolAsync("external");
        var chat = await Expect.JsonAsync<ConversationDto>(await member.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest(null, null, null, null)), HttpStatusCode.Created);
        var events = await Sse.ReadChatAsync(await member.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/messages",
            new SendMessageRequest("Send the reply to the customer", null, null, null, false, [read.Id.ToString("N"), send.Id.ToString("N")], null)));

        Assert.Contains(events, e => e.Event.Event == StreamEvents.ToolResult && e.Event.ToolId == read.Id.ToString("N"));
        var asked = Assert.Single(events, e => e.Event.Event == StreamEvents.ToolApproval).Event;
        Assert.Equal(("pending", send.Name), (asked.State, asked.Name));
        var ticket = Assert.Single(await admin.GetJsonAsync<IReadOnlyList<ApprovalDto>>(ApiRoutes.Approvals + "?state=pending"), a => a.Title == send.Name);
        Assert.Equal(("chat", ToolRisks.External, null), (ticket.Source, ticket.Risk, ticket.RunId));
        Assert.Empty(Spy.Calls);
        var message = (await member.GetJsonAsync<IReadOnlyList<MessageDto>>($"{ApiRoutes.Conversations}/{chat.Id}/messages")).Last(m => m.Role == MessageRoles.Assistant);
        Assert.Contains(message.Blocks, b => b.Kind == "tool_call" && b.Title == send.Name && b.State == "pending");
    }
}
