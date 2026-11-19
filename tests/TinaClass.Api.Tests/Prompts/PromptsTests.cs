using System.Net;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Prompts;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Prompts;

namespace TinaClass.Api.Tests.Prompts;

public sealed class PromptsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(string role) => await api.SignInAsync((await api.CreateUserAsync(role)).Email);

    private static string NewCommand() => "/c" + Guid.NewGuid().ToString("N")[..10];

    private static async Task<PromptDto> NewAsync(HttpClient client, string? command = null, string visibility = "workspace", string user = "Review this: {{code}}", string system = "") =>
        await Expect.JsonAsync<PromptDto>(await client.PostJsonAsync(ApiRoutes.Prompts, new SavePromptRequest("Prompt " + Guid.NewGuid().ToString("N")[..6], command ?? NewCommand(), visibility, system, user, 0)), HttpStatusCode.Created);

    [Fact]
    public async Task Each_Role_Gets_The_Answer_Its_Permissions_Allow()
    {
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Prompts, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        using var viewer = await ClientAsync(Roles.Viewer);
        using var member = await ClientAsync(Roles.Member);
        var prompt = await NewAsync(member);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(ApiRoutes.Prompts, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{ApiRoutes.Prompts}/{prompt.Id}", Ct)).StatusCode);
        var save = new SavePromptRequest("x", NewCommand(), "workspace", "", "x", 0);
        await Expect.ProblemAsync(await viewer.PostJsonAsync(ApiRoutes.Prompts, save), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await viewer.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/run", new RunPromptRequest(null, null)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await viewer.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/publish", new PublishPromptRequest("")), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await viewer.SendDeleteAsync($"{ApiRoutes.Prompts}/{prompt.Id}"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task A_Prompt_Is_Checked_And_Its_Command_Is_Unique_Whatever_The_Case()
    {
        using var member = await ClientAsync(Roles.Member);
        var fields = await Expect.ProblemAsync(await member.PostJsonAsync(ApiRoutes.Prompts, new SavePromptRequest("", "review", "friends", new string('s', 8_001), "", 0)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["command", "name", "systemText", "visibility"], fields.Order(StringComparer.Ordinal));

        var command = NewCommand();
        var first = await NewAsync(member, command.ToUpperInvariant());
        Assert.Equal(command, first.Command);
        await Expect.ProblemAsync(await member.PostJsonAsync(ApiRoutes.Prompts, new SavePromptRequest("Twin", command, "workspace", "", "x", 0)), HttpStatusCode.Conflict, ErrorCodes.CommandTaken);
        var second = await NewAsync(member);
        await Expect.ProblemAsync(await member.PatchJsonAsync($"{ApiRoutes.Prompts}/{second.Id}", new SavePromptRequest(second.Name, command, "workspace", "", "x", second.Version)), HttpStatusCode.Conflict, ErrorCodes.CommandTaken);
        // Taking a command of one's own prompt again is not a clash.
        Assert.Equal(HttpStatusCode.OK, (await member.PatchJsonAsync($"{ApiRoutes.Prompts}/{first.Id}", new SavePromptRequest("Renamed", command, "workspace", "", "x", first.Version))).StatusCode);
    }

    [Fact]
    public async Task A_Stale_Version_Is_A_409()
    {
        using var member = await ClientAsync(Roles.Member);
        var prompt = await NewAsync(member);
        var save = new SavePromptRequest("New name", prompt.Command, "workspace", "", prompt.UserText, prompt.Version);
        Assert.Equal(HttpStatusCode.OK, (await member.PatchJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}", save)).StatusCode);
        await Expect.ProblemAsync(await member.PatchJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}", save with { Name = "Late" }), HttpStatusCode.Conflict, ErrorCodes.Conflict);
    }

    [Fact]
    public async Task A_Private_Prompt_Is_Invisible_And_A_Shared_One_Is_Read_Only_For_Others_But_Not_For_An_Administrator()
    {
        using var alice = await ClientAsync(Roles.Member);
        using var bob = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var secret = await NewAsync(alice, visibility: "private");
        var shared = await NewAsync(alice);
        var url = $"{ApiRoutes.Prompts}/{secret.Id}";

        Assert.DoesNotContain(await bob.GetJsonAsync<IReadOnlyList<PromptDto>>(ApiRoutes.Prompts), p => p.Id == secret.Id);
        Assert.DoesNotContain(await admin.GetJsonAsync<IReadOnlyList<PromptDto>>(ApiRoutes.Prompts), p => p.Id == secret.Id);
        foreach (var response in new[] { await bob.GetAsync(url, Ct), await bob.GetAsync(url + "/versions", Ct), await bob.GetAsync(url + "/cases", Ct),
                                         await bob.PostJsonAsync(url + "/run", new RunPromptRequest(null, null)), await bob.SendDeleteAsync(url), await admin.SendDeleteAsync(url) })
        {
            await Expect.ProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
        }

        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"{ApiRoutes.Prompts}/{shared.Id}", Ct)).StatusCode);
        await Expect.ProblemAsync(await bob.PatchJsonAsync($"{ApiRoutes.Prompts}/{shared.Id}", new SavePromptRequest("Hijack", shared.Command, "workspace", "", "x", shared.Version)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await bob.SendDeleteAsync($"{ApiRoutes.Prompts}/{shared.Id}"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendDeleteAsync($"{ApiRoutes.Prompts}/{shared.Id}")).StatusCode);
    }

    [Fact]
    public async Task Publishing_Copies_The_Draft_Into_A_Version_That_Later_Edits_Do_Not_Touch()
    {
        using var member = await ClientAsync(Roles.Member);
        var prompt = await NewAsync(member, user: "Version one {{x}}");
        Assert.Equal(0, prompt.CurrentVersion);
        var one = await Expect.JsonAsync<PromptDto>(await member.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/publish", new PublishPromptRequest("first")));
        Assert.Equal(1, one.CurrentVersion);
        var edited = await Expect.JsonAsync<PromptDto>(await member.PatchJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}", new SavePromptRequest(prompt.Name, prompt.Command, "workspace", "", "Version two {{x}}", one.Version)));
        Assert.Equal(1, edited.CurrentVersion);
        var two = await Expect.JsonAsync<PromptDto>(await member.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/publish", new PublishPromptRequest("second")));
        Assert.Equal(2, two.CurrentVersion);

        var versions = await member.GetJsonAsync<IReadOnlyList<PromptVersionDto>>($"{ApiRoutes.Prompts}/{prompt.Id}/versions");
        Assert.Equal([2, 1], versions.Select(v => v.Number));
        Assert.Equal(["Version two {{x}}", "Version one {{x}}"], versions.Select(v => v.UserText));
        Assert.Equal([true, false], versions.Select(v => v.Published));
        Assert.Contains(api.Events.Events, e => e.Type == PromptsEvents.Published && !e.Json.Contains("Version two"));

        var empty = await NewAsync(member, user: "");
        await Expect.ProblemAsync(await member.PostJsonAsync($"{ApiRoutes.Prompts}/{empty.Id}/publish", new PublishPromptRequest("")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [Fact]
    public async Task Trying_A_Prompt_Needs_Every_Variable_And_Answers_Through_The_Model()
    {
        using var member = await ClientAsync(Roles.Member);
        var prompt = await NewAsync(member, user: "Summarize {{topic}} for {{audience}}");
        Assert.Equal(["topic", "audience"], prompt.Variables);
        var fields = await Expect.ProblemAsync(await member.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/run",
            new RunPromptRequest(new Dictionary<string, string> { ["topic"] = "exports" }, null)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["variables"], fields);
        var run = await Expect.JsonAsync<RunPromptResponse>(await member.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/run",
            new RunPromptRequest(new Dictionary<string, string> { ["topic"] = "exports", ["audience"] = "support" }, null)));
        Assert.Contains("Summarize exports for support", run.Output);
        Assert.True(run.TokensOut > 0);
    }

    [Fact]
    public async Task Test_Cases_Pass_Or_Fail_On_The_Expected_Text_And_Keep_The_Result()
    {
        using var member = await ClientAsync(Roles.Member);
        var prompt = await NewAsync(member, user: "Review {{code}}");
        var cases = $"{ApiRoutes.Prompts}/{prompt.Id}/cases";
        var pass = await Expect.JsonAsync<PromptCaseDto>(await member.PostJsonAsync(cases, new SavePromptCaseRequest("echoes", "{\"code\":\"x = 1\"}", "x = 1")), HttpStatusCode.Created);
        var fail = await Expect.JsonAsync<PromptCaseDto>(await member.PostJsonAsync(cases, new SavePromptCaseRequest("never", "{\"code\":\"y\"}", "text the model never says")), HttpStatusCode.Created);
        Assert.Null(pass.Passed);

        var run = await Expect.JsonAsync<CasesRunDto>(await member.PostAsync(cases + "/run", null, Ct));
        Assert.Equal((1, 2), (run.Passed, run.Total));
        Assert.Equal([true, false], run.Cases.Select(c => c.Passed));
        var stored = await member.GetJsonAsync<PromptDto>($"{ApiRoutes.Prompts}/{prompt.Id}");
        Assert.Equal((1, 2), (stored.CasesPassed, stored.CasesTotal));

        // Editing a case clears its result.
        var updated = await Expect.JsonAsync<PromptCaseDto>(await member.PutJsonAsync($"{cases}/{fail.Id}", new SavePromptCaseRequest("now passes", "{\"code\":\"y\"}", "y")));
        Assert.Null(updated.Passed);
        Assert.Equal(HttpStatusCode.NoContent, (await member.SendDeleteAsync($"{cases}/{fail.Id}")).StatusCode);
        await Expect.ProblemAsync(await member.SendDeleteAsync($"{cases}/{fail.Id}"), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Case_Is_Checked_And_A_Prompt_Holds_At_Most_Twenty()
    {
        using var member = await ClientAsync(Roles.Member);
        var prompt = await NewAsync(member);
        var cases = $"{ApiRoutes.Prompts}/{prompt.Id}/cases";
        var fields = await Expect.ProblemAsync(await member.PostJsonAsync(cases, new SavePromptCaseRequest("", "not json", "")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["input", "name"], fields.Order(StringComparer.Ordinal));
        for (var i = 0; i < PromptsService.MaxCases; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await member.PostJsonAsync(cases, new SavePromptCaseRequest($"case {i}", "{}", ""))).StatusCode);
        }
        await Expect.ProblemAsync(await member.PostJsonAsync(cases, new SavePromptCaseRequest("one too many", "{}", "")), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        using var other = await ClientAsync(Roles.Member);
        await Expect.ProblemAsync(await other.PostJsonAsync(cases, new SavePromptCaseRequest("mine now", "{}", "")), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task A_Slash_Command_In_Chat_Expands_The_Published_Prompt_And_Counts_The_Use()
    {
        using var alice = await ClientAsync(Roles.Member);
        using var bob = await ClientAsync(Roles.Member);
        var command = NewCommand();
        var prompt = await NewAsync(alice, command, user: "Please review this code: {{code}}");
        var chat = await Expect.JsonAsync<ConversationDto>(await bob.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest(null, null, null, null)), HttpStatusCode.Created);
        async Task<string> SendAsync(string text)
        {
            await Sse.ReadChatAsync(await bob.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/messages", new SendMessageRequest(text, null, null, null, false, null, null)));
            return (await bob.GetJsonAsync<IReadOnlyList<MessageDto>>($"{ApiRoutes.Conversations}/{chat.Id}/messages")).Last(m => m.Role == MessageRoles.User).Blocks[0].Text!;
        }

        // Not published yet: the text goes through as written.
        Assert.Equal($"{command} a + b", await SendAsync($"{command} a + b"));
        await Expect.JsonAsync<PromptDto>(await alice.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/publish", new PublishPromptRequest("v1")));
        Assert.Equal("Please review this code: a + b", await SendAsync($"{command} a + b"));
        Assert.Equal(1, (await bob.GetJsonAsync<PromptDto>($"{ApiRoutes.Prompts}/{prompt.Id}")).UseCount);
        Assert.Equal("/nosuchcommand text", await SendAsync("/nosuchcommand text"));
    }

    [Fact]
    public async Task A_Private_Command_Is_Not_Expanded_For_Somebody_Else()
    {
        using var alice = await ClientAsync(Roles.Member);
        using var bob = await ClientAsync(Roles.Member);
        var command = NewCommand();
        var prompt = await NewAsync(alice, command, "private", "Secret instructions {{x}}");
        await Expect.JsonAsync<PromptDto>(await alice.PostJsonAsync($"{ApiRoutes.Prompts}/{prompt.Id}/publish", new PublishPromptRequest("")));
        var chat = await Expect.JsonAsync<ConversationDto>(await bob.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest(null, null, null, null)), HttpStatusCode.Created);
        await Sse.ReadChatAsync(await bob.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/messages", new SendMessageRequest($"{command} hello", null, null, null, false, null, null)));
        var text = (await bob.GetJsonAsync<IReadOnlyList<MessageDto>>($"{ApiRoutes.Conversations}/{chat.Id}/messages")).First().Blocks[0].Text!;
        Assert.Equal($"{command} hello", text);
        Assert.DoesNotContain("Secret instructions", text);
    }
}
