using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Chat;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Tests.Chat;

public sealed class ChatTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(string role) => await api.SignInAsync((await api.CreateUserAsync(role)).Email);

    private static async Task<ConversationDto> NewChatAsync(HttpClient client, string? title = null) =>
        await Expect.JsonAsync<ConversationDto>(await client.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest(title, null, null, null)), HttpStatusCode.Created);

    private static SendMessageRequest Say(string text, Guid? parent = null) => new(text, parent, null, null, false, null, null);

    private static async Task<IReadOnlyList<(int Id, ChatStreamEvent Event)>> AskAsync(HttpClient client, Guid conversationId, string text, Guid? parent = null) =>
        await Sse.ReadChatAsync(await client.PostJsonAsync($"{ApiRoutes.Conversations}/{conversationId}/messages", Say(text, parent)));

    [Fact]
    public async Task Every_Role_That_May_Chat_Can_And_Anonymous_Cannot()
    {
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Conversations, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        foreach (var role in new[] { Roles.Viewer, Roles.Member, Roles.Admin, Roles.Owner })
        {
            using var client = await ClientAsync(role);
            var chat = await NewChatAsync(client);
            Assert.Equal(Roles.Owner == role ? chat.OwnerId : chat.OwnerId, chat.OwnerId);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{ApiRoutes.Conversations}/{chat.Id}", Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task An_Answer_Streams_As_Events_In_Order_And_Is_Stored()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client);
        var events = await AskAsync(client, chat.Id, "Hello there");

        var names = events.Select(e => e.Event.Event).ToList();
        Assert.Equal(StreamEvents.MessageStart, names.First());
        Assert.Equal(StreamEvents.MessageDone, names.Last());
        Assert.True(names.IndexOf(StreamEvents.TextDelta) < names.IndexOf(StreamEvents.Usage));
        Assert.True(names.IndexOf(StreamEvents.Usage) < names.IndexOf(StreamEvents.MessageDone));
        Assert.Equal(events.Select(e => e.Id).Order(), events.Select(e => e.Id));
        Assert.Equal(events.Count, events.Select(e => e.Id).Distinct().Count());
        var start = events.First().Event;
        Assert.NotNull(start.MessageId);
        Assert.NotNull(start.UserMessageId);

        var streamed = string.Concat(events.Where(e => e.Event.Event == StreamEvents.TextDelta).Select(e => e.Event.Text));
        Assert.Contains("Hello there", streamed);
        var messages = await client.GetJsonAsync<IReadOnlyList<MessageDto>>($"{ApiRoutes.Conversations}/{chat.Id}/messages");
        Assert.Equal([MessageRoles.User, MessageRoles.Assistant], messages.Select(m => m.Role));
        var answer = messages[1];
        Assert.Equal((start.MessageId, MessageStatuses.Done), (answer.Id, answer.Status));
        Assert.Equal(streamed, string.Concat(answer.Blocks.Where(b => b.Kind == "text").Select(b => b.Text)));
        Assert.NotNull(answer.Usage);
        Assert.True(answer.Usage!.TokensOut > 0);

        var after = await client.GetJsonAsync<ConversationDto>($"{ApiRoutes.Conversations}/{chat.Id}");
        Assert.Equal(answer.Id, after.ActiveLeafId);
        Assert.Equal("Hello there", after.Title);
        Assert.True(after.TokenTotal > 0);
    }

    [Fact]
    public async Task The_Export_Question_Gets_The_Sample_Answer_With_Reasoning_And_A_Plan()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client);
        var names = (await AskAsync(client, chat.Id, "整理导出改版的痛点和优先级")).Select(e => e.Event.Event).Distinct().ToList();
        foreach (var expected in new[] { StreamEvents.ReasoningDelta, StreamEvents.ReasoningEnd, StreamEvents.Plan, StreamEvents.TextDelta, StreamEvents.Usage })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public async Task A_Message_Is_Checked_Before_Anything_Is_Written()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client);
        var url = $"{ApiRoutes.Conversations}/{chat.Id}/messages";
        await Expect.ProblemAsync(await client.PostJsonAsync(url, Say("  ")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        await Expect.ProblemAsync(await client.PostJsonAsync(url, Say(new string('x', ChatGenerator.MaxText + 1))), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        await Expect.ProblemAsync(await client.PostJsonAsync(url, Say("hi") with { Effort = "maximum" }), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        await Expect.ProblemAsync(await client.PostJsonAsync(url, Say("hi", Guid.NewGuid())), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Empty(await client.GetJsonAsync<IReadOnlyList<MessageDto>>(url));
    }

    [Fact]
    public async Task Somebody_Elses_Conversation_Is_A_404_Except_For_A_History_Manager_Who_May_Read_It()
    {
        using var alice = await ClientAsync(Roles.Member);
        using var bob = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        var chat = await NewChatAsync(alice, "Private");
        await AskAsync(alice, chat.Id, "secret plans");
        var url = $"{ApiRoutes.Conversations}/{chat.Id}";

        await Expect.ProblemAsync(await bob.GetAsync(url, Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await bob.GetAsync(url + "/messages", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await bob.PostJsonAsync(url + "/messages", Say("hi")), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await bob.PatchJsonAsync(url, new UpdateConversationRequest("x", null, false, null, null, null, null, 0)), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await bob.SendDeleteAsync(url), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await bob.PostJsonAsync(url + "/share", new CreateShareRequest(null)), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.DoesNotContain((await bob.GetJsonAsync<Paged<ConversationDto>>(ApiRoutes.Conversations + "?all=true")).Items, c => c.Id == chat.Id);
        Assert.DoesNotContain((await bob.GetJsonAsync<IReadOnlyList<SearchResultDto>>(ApiRoutes.Search + "?q=secret")), r => r.Id == chat.Id);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url, Ct)).StatusCode);
        Assert.Contains((await admin.GetJsonAsync<Paged<ConversationDto>>(ApiRoutes.Conversations + "?all=true")).Items, c => c.Id == chat.Id);
        await Expect.ProblemAsync(await admin.PostJsonAsync(url + "/messages", Say("hi")), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Stale_Version_Is_A_409_And_A_Rename_Sticks_Against_The_Auto_Title()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client);
        var renamed = await Expect.JsonAsync<ConversationDto>(await client.PatchJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}",
            new UpdateConversationRequest("My title", null, false, true, null, null, null, chat.Version)));
        Assert.Equal(("My title", true), (renamed.Title, renamed.Pinned));
        await Expect.ProblemAsync(await client.PatchJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}",
            new UpdateConversationRequest("Late", null, false, null, null, null, null, chat.Version)), HttpStatusCode.Conflict, ErrorCodes.Conflict);
        await AskAsync(client, chat.Id, "something else entirely");
        Assert.Equal("My title", (await client.GetJsonAsync<ConversationDto>($"{ApiRoutes.Conversations}/{chat.Id}")).Title);
    }

    [Fact]
    public async Task Writing_An_Answer_Again_Makes_A_Sibling_And_The_Branch_Can_Be_Chosen()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client);
        var first = (await AskAsync(client, chat.Id, "branch me")).First().Event;
        var regenerated = (await Sse.ReadChatAsync(await client.PostJsonAsync($"{ApiRoutes.Messages}/{first.MessageId}/regenerate", new RegenerateRequest(null)))).First().Event;
        Assert.NotEqual(first.MessageId, regenerated.MessageId);

        var branch = await client.GetJsonAsync<IReadOnlyList<MessageDto>>($"{ApiRoutes.Conversations}/{chat.Id}/messages");
        var answer = branch.Last();
        Assert.Equal(regenerated.MessageId, answer.Id);
        Assert.Equal((2, 1), (answer.SiblingCount, answer.SiblingIndex));
        Assert.Equal(first.UserMessageId, answer.ParentId);

        var older = await client.GetJsonAsync<IReadOnlyList<MessageDto>>($"{ApiRoutes.Conversations}/{chat.Id}/messages?leaf={first.MessageId}");
        Assert.Equal(first.MessageId, older.Last().Id);
        await Expect.ProblemAsync(await client.PostJsonAsync($"{ApiRoutes.Messages}/{first.UserMessageId}/regenerate", new RegenerateRequest(null)), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
    }

    [Fact]
    public async Task Feedback_Is_Kept_On_An_Answer_Only()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client);
        var start = (await AskAsync(client, chat.Id, "rate me")).First().Event;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync($"{ApiRoutes.Messages}/{start.MessageId}/feedback", new FeedbackRequest("up"))).StatusCode);
        Assert.Equal("up", (await client.GetJsonAsync<IReadOnlyList<MessageDto>>($"{ApiRoutes.Conversations}/{chat.Id}/messages")).Last().Feedback);
        await Expect.ProblemAsync(await client.PostJsonAsync($"{ApiRoutes.Messages}/{start.MessageId}/feedback", new FeedbackRequest("meh")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        await Expect.ProblemAsync(await client.PostJsonAsync($"{ApiRoutes.Messages}/{start.UserMessageId}/feedback", new FeedbackRequest("up")), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
    }

    [Fact]
    public async Task Folders_Nest_To_A_Limit_And_Deleting_One_Frees_Its_Conversations()
    {
        using var client = await ClientAsync(Roles.Member);
        Guid? parent = null;
        FolderDto folder = null!;
        for (var depth = 1; depth <= ChatService.MaxFolderDepth; depth++)
        {
            folder = await Expect.JsonAsync<FolderDto>(await client.PostJsonAsync(ApiRoutes.Folders, new CreateFolderRequest($"level {depth}", parent)), HttpStatusCode.Created);
            parent = folder.Id;
        }
        var fields = await Expect.ProblemAsync(await client.PostJsonAsync(ApiRoutes.Folders, new CreateFolderRequest("too deep", parent)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["parentId"], fields);

        var chat = await Expect.JsonAsync<ConversationDto>(
            await client.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest("inside", folder.Id, null, null)), HttpStatusCode.Created);
        var folders = await client.GetJsonAsync<IReadOnlyList<FolderDto>>(ApiRoutes.Folders);
        Assert.Equal(1, folders.Single(f => f.Id == folder.Id).ConversationCount);

        var top = folders.Single(f => f.ParentId is null);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendDeleteAsync($"{ApiRoutes.Folders}/{top.Id}")).StatusCode);
        Assert.Empty(await client.GetJsonAsync<IReadOnlyList<FolderDto>>(ApiRoutes.Folders));
        Assert.Null((await client.GetJsonAsync<ConversationDto>($"{ApiRoutes.Conversations}/{chat.Id}")).FolderId);
    }

    [Fact]
    public async Task Somebody_Elses_Folder_Cannot_Be_Used()
    {
        using var alice = await ClientAsync(Roles.Member);
        using var bob = await ClientAsync(Roles.Member);
        var folder = await Expect.JsonAsync<FolderDto>(await alice.PostJsonAsync(ApiRoutes.Folders, new CreateFolderRequest("mine", null)), HttpStatusCode.Created);
        await Expect.ProblemAsync(await bob.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest("x", folder.Id, null, null)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        await Expect.ProblemAsync(await bob.SendDeleteAsync($"{ApiRoutes.Folders}/{folder.Id}"), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Share_Link_Shows_A_Snapshot_Without_Signing_In_Until_It_Is_Revoked()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client, "Shared chat");
        await Expect.ProblemAsync(await client.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/share", new CreateShareRequest(null)), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        await AskAsync(client, chat.Id, "share this");
        var share = await Expect.JsonAsync<ShareDto>(await client.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/share", new CreateShareRequest(null)));

        using var anonymous = api.CreateClient();
        var shown = await Expect.JsonAsync<SharedConversationDto>(await anonymous.GetAsync($"{ApiRoutes.Shared}/{share.Token}", Ct));
        Assert.Equal("Shared chat", shown.Title);
        Assert.Equal(2, shown.Messages.Count);
        Assert.Contains(api.Audit.Records, r => r.Action == ChatAuditActions.Shared && r.ResourceId == chat.Id.ToString());

        // Later messages are not in the snapshot.
        await AskAsync(client, chat.Id, "more");
        Assert.Equal(2, (await Expect.JsonAsync<SharedConversationDto>(await anonymous.GetAsync($"{ApiRoutes.Shared}/{share.Token}", Ct))).Messages.Count);

        await Expect.ProblemAsync(await anonymous.GetAsync($"{ApiRoutes.Shared}/not-a-token", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendDeleteAsync($"{ApiRoutes.Shares}/{share.Id}")).StatusCode);
        await Expect.ProblemAsync(await anonymous.GetAsync($"{ApiRoutes.Shared}/{share.Token}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Link_Expires_And_The_Token_Is_Never_Stored()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client);
        await AskAsync(client, chat.Id, "expire");
        var share = await Expect.JsonAsync<ShareDto>(await client.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/share", new CreateShareRequest(1)));
        Assert.NotNull(share.ExpiresAt);
        var stored = await api.InScopeAsync(async s => await Task.FromResult(string.Join("|", s.GetRequiredService<TinaClass.Api.Features.Chat.Data.ChatDbContext>().Shares.Select(x => x.TokenHash))));
        Assert.DoesNotContain(share.Token, stored);

        using var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"{ApiRoutes.Shared}/{share.Token}", Ct)).StatusCode);
        await Expect.ProblemAsync(await client.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/share", new CreateShareRequest(0)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        api.Time.Advance(TimeSpan.FromDays(2));
        await Expect.ProblemAsync(await anonymous.GetAsync($"{ApiRoutes.Shared}/{share.Token}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Viewer_Cannot_Share_And_Someone_Elses_Link_Cannot_Be_Revoked()
    {
        using var viewer = await ClientAsync(Roles.Viewer);
        var chat = await NewChatAsync(viewer);
        await AskAsync(viewer, chat.Id, "viewing");
        await Expect.ProblemAsync(await viewer.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/share", new CreateShareRequest(null)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);

        using var alice = await ClientAsync(Roles.Member);
        using var bob = await ClientAsync(Roles.Member);
        var mine = await NewChatAsync(alice);
        await AskAsync(alice, mine.Id, "mine");
        var share = await Expect.JsonAsync<ShareDto>(await alice.PostJsonAsync($"{ApiRoutes.Conversations}/{mine.Id}/share", new CreateShareRequest(null)));
        await Expect.ProblemAsync(await bob.SendDeleteAsync($"{ApiRoutes.Shares}/{share.Id}"), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_Deleted_Conversation_Disappears_At_Once_And_Is_Purged_After_The_Grace_Period()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client);
        await AskAsync(client, chat.Id, "to be deleted");
        var share = await Expect.JsonAsync<ShareDto>(await client.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/share", new CreateShareRequest(null)));
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendDeleteAsync($"{ApiRoutes.Conversations}/{chat.Id}")).StatusCode);
        await Expect.ProblemAsync(await client.GetAsync($"{ApiRoutes.Conversations}/{chat.Id}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        Assert.Contains(api.Audit.Records, r => r.Action == ChatAuditActions.ConversationDeleted && r.ResourceId == chat.Id.ToString());

        async Task<int> PurgeAsync() => await api.InScopeAsync(s => s.GetRequiredService<ChatPurger>().RunAsync(Ct));
        Assert.Equal(0, await PurgeAsync());
        api.Time.Advance(TimeSpan.FromDays(31));
        Assert.True(await PurgeAsync() >= 1);
        await api.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<TinaClass.Api.Features.Chat.Data.ChatDbContext>();
            Assert.Empty(db.Messages.Where(m => m.ConversationId == chat.Id));
            Assert.Empty(db.Shares.Where(x => x.ConversationId == chat.Id));
            return await Task.FromResult(0);
        });
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync($"{ApiRoutes.Shared}/{share.Token}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Search_Finds_Own_Conversations_By_Title_And_By_Message_Text()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client, "Quarterly Zebra report");
        await AskAsync(client, chat.Id, "talk about quokkas");
        var byTitle = await client.GetJsonAsync<IReadOnlyList<SearchResultDto>>(ApiRoutes.Search + "?q=zebra&scope=conversations");
        Assert.Contains(byTitle, r => r.Id == chat.Id);
        var byText = await client.GetJsonAsync<IReadOnlyList<SearchResultDto>>(ApiRoutes.Search + "?q=quokkas&scope=conversations");
        Assert.Contains(byText, r => r.Id == chat.Id && r.Snippet.Contains("quokkas"));
        Assert.Empty(await client.GetJsonAsync<IReadOnlyList<SearchResultDto>>(ApiRoutes.Search + "?q=&scope=conversations"));
    }

    [Fact]
    public async Task Creating_And_Finishing_Publish_Events_Without_Message_Text()
    {
        using var client = await ClientAsync(Roles.Member);
        var chat = await NewChatAsync(client);
        await AskAsync(client, chat.Id, "event text must not leak");
        await WaitAsync(() => api.Events.Events.Any(e => e.Type == ChatEvents.MessageCompleted && e.Json.Contains(chat.Id.ToString())));
        Assert.Contains(api.Events.Events, e => e.Type == ChatEvents.ConversationCreated && e.Json.Contains(chat.Id.ToString()));
        Assert.DoesNotContain(api.Events.Events, e => e.Json.Contains("event text must not leak"));
    }

    [Fact]
    public async Task Stopping_Keeps_What_Was_Written_And_A_Dropped_Connection_Can_Be_Picked_Up()
    {
        var user = await api.CreateUserAsync(Roles.Member);
        var login = await api.LoginAsync(user.Email);
        await using var slow = api.WithWebHostBuilder(b => b.UseSetting("Models:Echo:DelayMs", "60"));
        using var client = slow.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var chat = await NewChatAsync(client);

        // Read the start and a few pieces, drop the connection, then reconnect from the last event seen.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiRoutes.Conversations}/{chat.Id}/messages") { Content = JsonContent.Create(Say("tell me something long"), options: ApiFactory.Json) };
        Guid messageId;
        var seen = new List<(int Id, ChatStreamEvent Event)>();
        using (var live = await LiveSse.OpenAsync(client, request))
        {
            while (seen.Count(e => e.Event.Event == StreamEvents.TextDelta) < 2)
            {
                seen.Add((await live.NextAsync())!.Value);
            }
            messageId = seen.First().Event.MessageId!.Value;
        }
        var lastId = seen.Last().Id;
        using var resume = new HttpRequestMessage(HttpMethod.Get, $"{ApiRoutes.Messages}/{messageId}/stream");
        resume.Headers.Add("Last-Event-ID", lastId.ToString());
        using var again = await LiveSse.OpenAsync(client, resume);
        var next = (await again.NextAsync())!.Value;
        Assert.Equal(lastId + 1, next.Id);

        // Now stop it: the rest of the stream ends with the stopped state and the partial text stays.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"{ApiRoutes.Messages}/{messageId}/stop", null, Ct)).StatusCode);
        (int Id, ChatStreamEvent Event) last = next;
        while (await again.NextAsync() is { } more)
        {
            last = more;
        }
        Assert.Equal(StreamEvents.MessageDone, last.Event.Event);
        Assert.Equal(MessageStatuses.Stopped, last.Event.Status);
        var answer = (await client.GetJsonAsync<IReadOnlyList<MessageDto>>($"{ApiRoutes.Conversations}/{chat.Id}/messages")).Last();
        Assert.Equal(MessageStatuses.Stopped, answer.Status);
        Assert.False(string.IsNullOrEmpty(string.Concat(answer.Blocks.Where(b => b.Kind == "text").Select(b => b.Text))));
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50, Ct);
        }
        Assert.True(condition(), "The condition did not become true in time.");
    }
}
