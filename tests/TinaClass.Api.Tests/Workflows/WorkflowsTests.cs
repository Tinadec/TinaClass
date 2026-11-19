using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Workflows;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Api.Tests.Usage;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Usage;
using TinaClass.Contracts.Workflows;

namespace TinaClass.Api.Tests.Workflows;

public sealed class WorkflowsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(string role) => await api.SignInAsync((await api.CreateUserAsync(role)).Email);

    private static FlowNodeDto N(string key, string kind, string title = "") => new(key, kind, string.IsNullOrEmpty(title) ? key : title, "", 0, 0);

    private static FlowEdgeDto E(string from, string to, string? label = null) => new(from, to, label);

    private static FlowGraphDto Graph(FlowNodeDto[] nodes, params FlowEdgeDto[] edges) => new(nodes, edges);

    /// <summary>A published flow made of <paramref name="graph"/>.</summary>
    private async Task<FlowDto> PublishedAsync(HttpClient admin, FlowGraphDto graph)
    {
        var flow = await Expect.JsonAsync<FlowDto>(await admin.PostJsonAsync(ApiRoutes.Flows, new CreateFlowRequest("Flow " + Guid.NewGuid().ToString("N")[..6])), HttpStatusCode.Created);
        flow = await Expect.JsonAsync<FlowDto>(await admin.PutJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/draft", new PutDraftRequest(graph, flow.Version)));
        return await Expect.JsonAsync<FlowDto>(await admin.PostAsync($"{ApiRoutes.Flows}/{flow.Id}/publish", null, Ct));
    }

    private async Task<FlowRunDto> RunAsync(HttpClient client, FlowDto flow, string input)
    {
        var started = await Expect.JsonAsync<FlowRunDto>(await client.PostJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/runs", new StartFlowRunRequest(input)), HttpStatusCode.Accepted);
        return await IdleAsync(client, started.Id);
    }

    private async Task<FlowRunDto> IdleAsync(HttpClient client, Guid runId)
    {
        await api.Services.GetRequiredService<FlowRunner>().WhenIdleAsync(runId).WaitAsync(TimeSpan.FromSeconds(20), Ct);
        return await client.GetJsonAsync<FlowRunDto>($"{ApiRoutes.FlowRuns}/{runId}");
    }

    private static string StateOf(FlowRunDto run, string key) => run.Nodes.Single(n => n.Key == key).State;

    [Fact]
    public async Task Each_Role_Gets_The_Answer_Its_Permissions_Allow()
    {
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Flows, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        using var viewer = await ClientAsync(Roles.Viewer);
        using var member = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var flow = await PublishedAsync(admin, Graph([N("a", NodeKinds.Trigger), N("b", NodeKinds.Send)], E("a", "b")));

        await Expect.ProblemAsync(await viewer.GetAsync(ApiRoutes.Flows, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await viewer.PostJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/runs", new StartFlowRunRequest("x")), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        // A member sees and runs flows but does not change them.
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"{ApiRoutes.Flows}/{flow.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"{ApiRoutes.Flows}/{flow.Id}/versions", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await member.PostJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/runs", new StartFlowRunRequest("x"))).StatusCode);
        await Expect.ProblemAsync(await member.PostJsonAsync(ApiRoutes.Flows, new CreateFlowRequest("Mine")), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await member.PutJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/draft", new PutDraftRequest(flow.Draft, flow.Version)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await member.PostAsync($"{ApiRoutes.Flows}/{flow.Id}/publish", null, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await member.SendDeleteAsync($"{ApiRoutes.Flows}/{flow.Id}"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await admin.GetAsync($"{ApiRoutes.Flows}/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_New_Flow_Starts_With_A_Trigger_And_Cannot_Be_Published_Without_Anything_After_It()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var fields = await Expect.ProblemAsync(await admin.PostJsonAsync(ApiRoutes.Flows, new CreateFlowRequest("")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["name"], fields);
        var flow = await Expect.JsonAsync<FlowDto>(await admin.PostJsonAsync(ApiRoutes.Flows, new CreateFlowRequest("  Triage  ")), HttpStatusCode.Created);
        Assert.Equal(("Triage", 0), (flow.Name, flow.PublishedVersion));
        Assert.Equal(NodeKinds.Trigger, Assert.Single(flow.Draft.Nodes).Kind);
        Assert.True(flow.HasChanges);
        Assert.Equal(["nodes"], await Expect.ProblemAsync(await admin.PostAsync($"{ApiRoutes.Flows}/{flow.Id}/publish", null, Ct), HttpStatusCode.BadRequest, ErrorCodes.Validation));
        await Expect.ProblemAsync(await admin.PostJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/runs", new StartFlowRunRequest("go")), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        Assert.Contains(api.Audit.Records, r => r.Action == WorkflowsAudit.Created && r.ResourceId == flow.Id.ToString());
    }

    [Fact]
    public async Task A_Draft_Must_Be_A_Sound_Graph()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var flow = await Expect.JsonAsync<FlowDto>(await admin.PostJsonAsync(ApiRoutes.Flows, new CreateFlowRequest("Checks")), HttpStatusCode.Created);
        async Task<IReadOnlyList<string>> BadAsync(FlowGraphDto graph) =>
            await Expect.ProblemAsync(await admin.PutJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/draft", new PutDraftRequest(graph, flow.Version)), HttpStatusCode.BadRequest, ErrorCodes.Validation);

        Assert.Equal(["edges"], await BadAsync(Graph([N("a", NodeKinds.Trigger), N("b", NodeKinds.Model), N("c", NodeKinds.Send)], E("a", "b"), E("b", "c"), E("c", "b"))));
        Assert.Equal(["nodes"], await BadAsync(Graph([N("a", NodeKinds.Trigger), N("a", NodeKinds.Send)])));
        Assert.Equal(["nodes"], await BadAsync(Graph([N("a", NodeKinds.Trigger), N("b", "teleport")])));
        Assert.Equal(["nodes"], await BadAsync(Graph([N("a b", NodeKinds.Trigger)])));
        Assert.Equal(["edges"], await BadAsync(Graph([N("a", NodeKinds.Trigger)], E("a", "ghost"))));
        Assert.Equal(["edges"], await BadAsync(Graph([N("a", NodeKinds.Trigger), N("b", NodeKinds.Send)], E("a", "b"), E("a", "b"))));
        Assert.Equal(["edges"], await BadAsync(Graph([N("a", NodeKinds.Trigger)], E("a", "a"))));
        Assert.Equal(["nodes"], await BadAsync(Graph([.. Enumerable.Range(0, FlowGraphs.MaxNodes + 1).Select(i => N("n" + i, NodeKinds.Send))])));
        // The draft may be incomplete (two triggers), a version may not.
        var twoTriggers = Graph([N("a", NodeKinds.Trigger), N("b", NodeKinds.Trigger)]);
        var saved = await Expect.JsonAsync<FlowDto>(await admin.PutJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/draft", new PutDraftRequest(twoTriggers, flow.Version)));
        Assert.Equal(["nodes"], await Expect.ProblemAsync(await admin.PostAsync($"{ApiRoutes.Flows}/{flow.Id}/publish", null, Ct), HttpStatusCode.BadRequest, ErrorCodes.Validation));
        await Expect.ProblemAsync(await admin.PutJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/draft", new PutDraftRequest(twoTriggers, flow.Version)), HttpStatusCode.Conflict, ErrorCodes.Conflict);
        Assert.True(saved.Version > flow.Version);
        Assert.Contains(api.Audit.Records, r => r.Action == WorkflowsAudit.DraftSaved && r.ResourceId == flow.Id.ToString());
    }

    [Fact]
    public async Task Publishing_Makes_An_Immutable_Version_And_A_Run_Uses_The_Latest_One()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var v1 = await PublishedAsync(admin, Graph([N("a", NodeKinds.Trigger), N("b", NodeKinds.Send)], E("a", "b")));
        Assert.Equal(1, v1.PublishedVersion);
        Assert.False(v1.HasChanges);
        Assert.Contains(api.Events.Events, e => e.Type == WorkflowsEvents.Published && e.Json.Contains(v1.Id.ToString()));

        // Editing the draft changes nothing that was published: the next run still has the two nodes of v1.
        var edited = await Expect.JsonAsync<FlowDto>(await admin.PutJsonAsync($"{ApiRoutes.Flows}/{v1.Id}/draft",
            new PutDraftRequest(Graph([N("a", NodeKinds.Trigger), N("b", NodeKinds.Send), N("c", NodeKinds.Tool)], E("a", "b"), E("b", "c")), v1.Version)));
        Assert.True(edited.HasChanges);
        var first = await RunAsync(admin, edited, "hello");
        Assert.Equal((1, 2), (first.FlowVersion, first.Nodes.Count));

        var v2 = await Expect.JsonAsync<FlowDto>(await admin.PostAsync($"{ApiRoutes.Flows}/{v1.Id}/publish", null, Ct));
        Assert.Equal(2, v2.PublishedVersion);
        var second = await RunAsync(admin, v2, "hello");
        Assert.Equal((2, 3), (second.FlowVersion, second.Nodes.Count));
        var versions = await admin.GetJsonAsync<IReadOnlyList<FlowVersionDto>>($"{ApiRoutes.Flows}/{v1.Id}/versions");
        Assert.Equal([2, 1], versions.Select(v => v.Number));
        Assert.Equal(first.Nodes.Count, (await admin.GetJsonAsync<FlowRunDto>($"{ApiRoutes.FlowRuns}/{first.Id}")).Nodes.Count);
    }

    [Fact]
    public async Task A_Run_Goes_Through_Every_Node_Counts_Up_And_Is_Listed()
    {
        using var member = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var flow = await PublishedAsync(admin, Graph([N("a", NodeKinds.Trigger), N("m", NodeKinds.Model, "Summarize"), N("t", NodeKinds.Tool), N("s", NodeKinds.Send)], E("a", "m"), E("m", "t"), E("t", "s")));
        var fields = await Expect.ProblemAsync(await member.PostJsonAsync($"{ApiRoutes.Flows}/{flow.Id}/runs", new StartFlowRunRequest(new string('x', 4_001))), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["input"], fields);

        var one = await RunAsync(member, flow, "Export hangs at 80 percent");
        var two = await RunAsync(member, flow, "again");
        Assert.Equal(FlowRunStates.Succeeded, one.State);
        Assert.Equal(["succeeded", "succeeded", "succeeded", "succeeded"], one.Nodes.Select(n => n.State));
        Assert.Equal(["a", "m", "t", "s"], one.Nodes.Select(n => n.Key));
        Assert.Equal("Export hangs at 80 percent", one.Nodes[0].Output);
        Assert.False(string.IsNullOrEmpty(one.Nodes[1].Output));
        Assert.NotNull(one.EndedAt);
        Assert.Equal(one.Ordinal + 1, two.Ordinal);
        Assert.Contains(api.Events.Events, e => e.Type == WorkflowsEvents.RunCompleted && e.Json.Contains(one.Id.ToString()));
        var listed = await member.GetJsonAsync<IReadOnlyList<FlowRunDto>>($"{ApiRoutes.Flows}/{flow.Id}/runs");
        Assert.Equal([two.Id, one.Id], listed.Select(r => r.Id));
        await Expect.ProblemAsync(await member.GetAsync($"{ApiRoutes.FlowRuns}/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Theory]
    [InlineData("导出卡在 80%，系统报错 error", "yes")]
    [InlineData("请问什么时候开发票", "no")]
    public async Task A_Branch_Follows_One_Edge_And_Skips_What_Only_The_Other_Leads_To(string input, string chosen)
    {
        using var admin = await ClientAsync(Roles.Admin);
        var flow = await PublishedAsync(admin, Graph([N("a", NodeKinds.Trigger), N("b", NodeKinds.Branch), N("yes", NodeKinds.Model), N("no", NodeKinds.Send), N("end", NodeKinds.Tool)],
            E("a", "b"), E("b", "yes", "yes"), E("b", "no", "no"), E("yes", "end"), E("no", "end")));
        var run = await RunAsync(admin, flow, input);
        Assert.Equal(FlowRunStates.Succeeded, run.State);
        Assert.Equal(chosen, run.Nodes.Single(n => n.Key == "b").Output);
        var other = chosen == "yes" ? "no" : "yes";
        Assert.Equal((NodeStates.Succeeded, NodeStates.Skipped), (StateOf(run, chosen), StateOf(run, other)));
        // The join runs when any edge into it is active.
        Assert.Equal(NodeStates.Succeeded, StateOf(run, "end"));
    }

    [Fact]
    public async Task An_Approval_Node_Waits_For_A_Person_And_The_Decision_Resumes_Or_Fails_The_Run()
    {
        using var admin = await ClientAsync(Roles.Admin);
        using var member = await ClientAsync(Roles.Member);
        var flow = await PublishedAsync(admin, Graph([N("a", NodeKinds.Trigger), N("ok", NodeKinds.Approval, "Confirm before sending"), N("s", NodeKinds.Send)], E("a", "ok"), E("ok", "s")));

        var waiting = await RunAsync(member, flow, "Reply to ticket 48213");
        Assert.Equal(FlowRunStates.Waiting, waiting.State);
        Assert.Equal((NodeStates.Succeeded, NodeStates.Waiting, NodeStates.Pending), (StateOf(waiting, "a"), StateOf(waiting, "ok"), StateOf(waiting, "s")));
        var pending = await admin.GetJsonAsync<IReadOnlyList<ApprovalDto>>(ApiRoutes.Approvals + "?state=pending");
        var ticket = Assert.Single(pending, a => a.Source == "flow" && a.Title == "Confirm before sending" && a.Detail.Contains("48213"));
        Assert.Equal(ToolRisks.External, ticket.Risk);
        await Expect.ProblemAsync(await member.PostJsonAsync($"{ApiRoutes.Approvals}/{ticket.Id}/approve", new DecideApprovalRequest(null)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await admin.SendDeleteAsync($"{ApiRoutes.Flows}/{flow.Id}"), HttpStatusCode.Conflict, ErrorCodes.InvalidState);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostJsonAsync($"{ApiRoutes.Approvals}/{ticket.Id}/approve", new DecideApprovalRequest(null))).StatusCode);
        var done = await IdleAsync(member, waiting.Id);
        Assert.Equal(FlowRunStates.Succeeded, done.State);
        Assert.Equal([NodeStates.Succeeded, NodeStates.Succeeded, NodeStates.Succeeded], done.Nodes.Select(n => n.State));
        Assert.Equal("Approved.", done.Nodes[1].Output);

        var second = await RunAsync(member, flow, "Reply to ticket 48214");
        var again = Assert.Single(await admin.GetJsonAsync<IReadOnlyList<ApprovalDto>>(ApiRoutes.Approvals + "?state=pending"), a => a.Source == "flow" && a.Detail.Contains("48214"));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostJsonAsync($"{ApiRoutes.Approvals}/{again.Id}/reject", new DecideApprovalRequest("Wrong customer."))).StatusCode);
        var rejected = await IdleAsync(member, second.Id);
        Assert.Equal(FlowRunStates.Failed, rejected.State);
        Assert.Equal(NodeStates.Failed, StateOf(rejected, "ok"));
        Assert.Contains("Wrong customer.", rejected.Nodes.Single(n => n.Key == "ok").Output);
        Assert.Equal(NodeStates.Skipped, StateOf(rejected, "s"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendDeleteAsync($"{ApiRoutes.Flows}/{flow.Id}")).StatusCode);
        await Expect.ProblemAsync(await admin.GetAsync($"{ApiRoutes.Flows}/{flow.Id}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Contains(api.Audit.Records, r => r.Action == WorkflowsAudit.Deleted && r.ResourceId == flow.Id.ToString());
    }

    [Fact]
    public async Task A_Spent_Budget_Fails_The_Model_Node_And_So_The_Run()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var flow = await PublishedAsync(admin, Graph([N("a", NodeKinds.Trigger), N("m", NodeKinds.Model), N("s", NodeKinds.Send)], E("a", "m"), E("m", "s")));
        await api.SpendAsync(null, 500);
        await admin.SetBudgetAsync(400, BudgetActions.Block);
        try
        {
            var run = await RunAsync(admin, flow, "anything");
            Assert.Equal(FlowRunStates.Failed, run.State);
            Assert.Equal((NodeStates.Failed, NodeStates.Skipped), (StateOf(run, "m"), StateOf(run, "s")));
            Assert.Contains("budget", run.Nodes.Single(n => n.Key == "m").Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await admin.SetBudgetAsync(0, BudgetActions.Notify);
        }
    }
}
