using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Agents;
using TinaClass.Api.Features.Agents.Data;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Agents;

public sealed class AgentsTests(ApiFactory api) : AgentsTestBase(api)
{
    [Fact]
    public async Task Each_Role_Gets_The_Answer_Its_Permissions_Allow()
    {
        using var anonymous = Host.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Agents, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        using var viewer = await ClientAsync(Roles.Viewer);
        using var member = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var agent = await NewAgentAsync(member);

        foreach (var url in new[] { ApiRoutes.Agents, $"{ApiRoutes.Agents}/{agent.Id}", ApiRoutes.Tools, ApiRoutes.Runs })
        {
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(url, Ct)).StatusCode);
        }
        var save = new SaveAgentRequest("x", "", "x", "", 5, 0, "workspace", null, null, null, null, 0);
        await Expect.ProblemAsync(await viewer.PostJsonAsync(ApiRoutes.Agents, save), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await viewer.PostAsync($"{ApiRoutes.Agents}/{agent.Id}/publish", null, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        foreach (var url in new[] { ApiRoutes.Approvals, ApiRoutes.ToolServers })
        {
            await Expect.ProblemAsync(await viewer.GetAsync(url, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
            await Expect.ProblemAsync(await member.GetAsync(url, Ct), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url, Ct)).StatusCode);
        }
        await Expect.ProblemAsync(await member.PatchJsonAsync($"{ApiRoutes.Tools}/{Guid.NewGuid()}", new UpdateToolRequest("read")), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await member.PostJsonAsync(ApiRoutes.ToolServers, new CreateToolServerRequest("a", "mcp", "https://a.example")), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task An_Agent_Is_Checked_And_Keeps_Its_Tools_In_Order()
    {
        using var member = await ClientAsync(Roles.Member);
        var read = await SeedToolAsync("read");
        var write = await SeedToolAsync("write");
        var fields = await Expect.ProblemAsync(await member.PostJsonAsync(ApiRoutes.Agents, new SaveAgentRequest("", "", new string('i', 8_001), "", 0, -1, "friends", null, null,
            [Use(read), Use(read), new AgentToolSetting(write.Id, true, "maybe")], null, 0)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["instruction", "maxCost", "maxSteps", "name", "tools", "visibility"], fields.Order(StringComparer.Ordinal));
        await Expect.ProblemAsync(await member.PostJsonAsync(ApiRoutes.Agents, new SaveAgentRequest("Ghost", "", "x", "", 5, 0, "workspace", null, null, [new AgentToolSetting(Guid.NewGuid(), true, "auto")], null, 0)),
            HttpStatusCode.BadRequest, ErrorCodes.Validation);

        var agent = await NewAgentAsync(member, [Use(write, ToolApprovals.Ask), Use(read, enabled: false)]);
        Assert.Equal([write.Name, read.Name], agent.Tools.Select(t => t.Name));
        Assert.Equal([ToolApprovals.Ask, ToolApprovals.Auto], agent.Tools.Select(t => t.Approval));
        Assert.Equal([true, false], agent.Tools.Select(t => t.Enabled));
        Assert.Equal("published", agent.State);
        Assert.Contains(await member.GetJsonAsync<IReadOnlyList<AgentDto>>(ApiRoutes.Agents), a => a.Id == agent.Id);
    }

    [Fact]
    public async Task A_Stale_Version_Is_A_409_And_Publishing_Needs_An_Instruction()
    {
        using var member = await ClientAsync(Roles.Member);
        var draft = await NewAgentAsync(member, publish: false, instruction: "");
        Assert.Equal("draft", draft.State);
        await Expect.ProblemAsync(await member.PostAsync($"{ApiRoutes.Agents}/{draft.Id}/publish", null, Ct), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        await Expect.ProblemAsync(await member.PostJsonAsync($"{ApiRoutes.Agents}/{draft.Id}/runs", new StartRunRequest("go")), HttpStatusCode.Conflict, ErrorCodes.InvalidState);

        var save = new SaveAgentRequest("Renamed", "", "Now with an instruction", "", 5, 0, "workspace", null, null, null, null, draft.Version);
        Assert.Equal(HttpStatusCode.OK, (await member.PatchJsonAsync($"{ApiRoutes.Agents}/{draft.Id}", save)).StatusCode);
        await Expect.ProblemAsync(await member.PatchJsonAsync($"{ApiRoutes.Agents}/{draft.Id}", save with { Name = "Late" }), HttpStatusCode.Conflict, ErrorCodes.Conflict);
        Assert.Equal("published", (await Expect.JsonAsync<AgentDto>(await member.PostAsync($"{ApiRoutes.Agents}/{draft.Id}/publish", null, Ct))).State);
    }

    [Fact]
    public async Task A_Private_Agent_Is_Invisible_And_A_Shared_One_Can_Only_Be_Changed_By_Its_Owner_Or_An_Administrator()
    {
        using var alice = await ClientAsync(Roles.Member);
        using var bob = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var secret = await NewAgentAsync(alice, visibility: "private");
        var shared = await NewAgentAsync(alice);

        Assert.DoesNotContain(await bob.GetJsonAsync<IReadOnlyList<AgentDto>>(ApiRoutes.Agents), a => a.Id == secret.Id);
        Assert.DoesNotContain(await admin.GetJsonAsync<IReadOnlyList<AgentDto>>(ApiRoutes.Agents), a => a.Id == secret.Id);
        await Expect.ProblemAsync(await bob.GetAsync($"{ApiRoutes.Agents}/{secret.Id}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await admin.PostJsonAsync($"{ApiRoutes.Agents}/{secret.Id}/runs", new StartRunRequest("peek")), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await admin.SendDeleteAsync($"{ApiRoutes.Agents}/{secret.Id}"), HttpStatusCode.NotFound, ErrorCodes.NotFound);

        var save = new SaveAgentRequest("Hijack", "", "x", "", 5, 0, "workspace", null, null, null, null, shared.Version);
        await Expect.ProblemAsync(await bob.PatchJsonAsync($"{ApiRoutes.Agents}/{shared.Id}", save), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await bob.SendDeleteAsync($"{ApiRoutes.Agents}/{shared.Id}"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchJsonAsync($"{ApiRoutes.Agents}/{shared.Id}", save)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendDeleteAsync($"{ApiRoutes.Agents}/{shared.Id}")).StatusCode);
    }

    [Fact]
    public async Task Changes_Are_Audited_And_An_Agent_With_A_Run_In_Progress_Cannot_Be_Deleted()
    {
        using var member = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var write = await SeedToolAsync("write");
        var agent = await NewAgentAsync(member, [Use(write, ToolApprovals.Ask)]);
        Assert.Contains(Api.Audit.Records, r => r.Action == AgentsAudit.AgentCreated && r.ResourceId == agent.Id.ToString());
        Assert.Contains(Api.Audit.Records, r => r.Action == AgentsAudit.AgentPublished && r.ResourceId == agent.Id.ToString());

        var run = await RunAsync(member, agent);
        Assert.Equal(RunStates.WaitingApproval, run.State);
        await Expect.ProblemAsync(await member.SendDeleteAsync($"{ApiRoutes.Agents}/{agent.Id}"), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        Assert.Equal(RunStates.Cancelled, (await Expect.JsonAsync<RunDto>(await member.PostAsync($"{ApiRoutes.Runs}/{run.Id}/cancel", null, Ct))).State);
        Assert.Equal(HttpStatusCode.NoContent, (await member.SendDeleteAsync($"{ApiRoutes.Agents}/{agent.Id}")).StatusCode);
        Assert.Contains(Api.Audit.Records, r => r.Action == AgentsAudit.AgentDeleted && r.ResourceId == agent.Id.ToString());
        Assert.DoesNotContain((await admin.GetJsonAsync<IReadOnlyList<ApprovalDto>>(ApiRoutes.Approvals)), a => a.RunId == run.Id);
    }

    [Fact]
    public async Task A_Tool_Server_Is_Checked_Added_Refreshed_And_Removed()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var fields = await Expect.ProblemAsync(await admin.PostJsonAsync(ApiRoutes.ToolServers, new CreateToolServerRequest("", "stdio", "ftp://nope")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["endpoint", "kind", "name"], fields.Order(StringComparer.Ordinal));

        var server = await Expect.JsonAsync<ToolServerDto>(await admin.PostJsonAsync(ApiRoutes.ToolServers, new CreateToolServerRequest("Real MCP", "mcp", "https://mcp.real.test/mcp")), HttpStatusCode.Created);
        Assert.Equal(("unchecked", 0), (server.Status, server.ToolCount));
        Assert.Contains(Api.Audit.Records, r => r.Action == AgentsAudit.ServerAdded && r.ResourceId == server.Id.ToString());

        Mcp.Tools.Clear();
        Mcp.Tools.AddRange([("search_issues", "Find issues"), ("create_issue", "Open an issue")]);
        var refreshed = await Expect.JsonAsync<ToolServerDto>(await admin.PostAsync($"{ApiRoutes.ToolServers}/{server.Id}/refresh", null, Ct));
        Assert.Equal(("ok", 2), (refreshed.Status, refreshed.ToolCount));
        Assert.NotNull(refreshed.LastSeenAt);
        var tools = (await admin.GetJsonAsync<IReadOnlyList<ToolDto>>(ApiRoutes.Tools)).Where(t => t.ServerId == server.Id).ToList();
        // New tools start as "write": an administrator lowers a harmless one.
        Assert.Equal([ToolRisks.Write, ToolRisks.Write], tools.Select(t => t.Risk));
        Assert.All(tools, t => Assert.Equal("Real MCP", t.ServerName));

        await Expect.JsonAsync<ToolDto>(await admin.PatchJsonAsync($"{ApiRoutes.Tools}/{tools[0].Id}", new UpdateToolRequest(ToolRisks.Read)));
        Mcp.Tools.Add(("list_labels", ""));
        var again = await Expect.JsonAsync<ToolServerDto>(await admin.PostAsync($"{ApiRoutes.ToolServers}/{server.Id}/refresh", null, Ct));
        Assert.Equal(3, again.ToolCount);
        var kept = (await admin.GetJsonAsync<IReadOnlyList<ToolDto>>(ApiRoutes.Tools)).Single(t => t.Id == tools[0].Id);
        Assert.Equal(ToolRisks.Read, kept.Risk);

        Mcp.Mode = "500";
        Assert.Equal("error", (await Expect.JsonAsync<ToolServerDto>(await admin.PostAsync($"{ApiRoutes.ToolServers}/{server.Id}/refresh", null, Ct))).Status);
        Mcp.Mode = "down";
        Assert.Equal("error", (await Expect.JsonAsync<ToolServerDto>(await admin.PostAsync($"{ApiRoutes.ToolServers}/{server.Id}/refresh", null, Ct))).Status);
        Mcp.Mode = "ok";

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendDeleteAsync($"{ApiRoutes.ToolServers}/{server.Id}")).StatusCode);
        Assert.Contains(Api.Audit.Records, r => r.Action == AgentsAudit.ServerRemoved && r.ResourceId == server.Id.ToString());
        Assert.DoesNotContain(await admin.GetJsonAsync<IReadOnlyList<ToolDto>>(ApiRoutes.Tools), t => t.ServerId == server.Id);
        await Expect.ProblemAsync(await admin.SendDeleteAsync($"{ApiRoutes.ToolServers}/{server.Id}"), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Demo_Server_Answers_Without_The_Network_And_The_Built_In_Tools_Stay()
    {
        using var admin = await ClientAsync(Roles.Admin);
        Mcp.Mode = "down";
        var demo = await Expect.JsonAsync<ToolServerDto>(await admin.PostJsonAsync(ApiRoutes.ToolServers, new CreateToolServerRequest("Demo", "mcp", "https://tickets.corp.example/mcp")), HttpStatusCode.Created);
        var refreshed = await Expect.JsonAsync<ToolServerDto>(await admin.PostAsync($"{ApiRoutes.ToolServers}/{demo.Id}/refresh", null, Ct));
        Mcp.Mode = "ok";
        Assert.Equal(("ok", 0), (refreshed.Status, refreshed.ToolCount));

        var builtin = await SeedToolAsync("read", serverKind: "builtin");
        await Expect.ProblemAsync(await admin.SendDeleteAsync($"{ApiRoutes.ToolServers}/{builtin.ServerId}"), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
    }

    [Fact]
    public async Task A_Tool_Risk_Is_Changed_Only_To_A_Known_Value_And_Audited()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var tool = await SeedToolAsync("write");
        var fields = await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Tools}/{tool.Id}", new UpdateToolRequest("harmless")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["risk"], fields);
        await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Tools}/{Guid.NewGuid()}", new UpdateToolRequest("read")), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Equal(ToolRisks.External, (await Expect.JsonAsync<ToolDto>(await admin.PatchJsonAsync($"{ApiRoutes.Tools}/{tool.Id}", new UpdateToolRequest(ToolRisks.External)))).Risk);
        Assert.Contains(Api.Audit.Records, r => r.Action == AgentsAuditActions.ToolChanged && r.ResourceId == tool.Id.ToString());
    }

    [Theory]
    [InlineData(ToolRisks.Read, ToolApprovals.Auto, false)]
    [InlineData(ToolRisks.Read, ToolApprovals.Ask, false)]
    [InlineData(ToolRisks.Read, ToolApprovals.Always, true)]
    [InlineData(ToolRisks.Write, ToolApprovals.Auto, false)]
    [InlineData(ToolRisks.Write, ToolApprovals.Ask, true)]
    [InlineData(ToolRisks.Write, ToolApprovals.Always, true)]
    [InlineData(ToolRisks.External, ToolApprovals.Auto, true)]
    [InlineData(ToolRisks.External, ToolApprovals.Ask, true)]
    public void A_Tool_That_Reaches_Outside_Always_Needs_A_Person(string risk, string approval, bool needed) => Assert.Equal(needed, RunExecutor.NeedsApproval(risk, approval));
}
