using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Knowledge;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Knowledge;

namespace TinaClass.Api.Tests.Knowledge;

public sealed class KnowledgeTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> ClientAsync(string role) => await api.SignInAsync((await api.CreateUserAsync(role)).Email);

    private Task DrainAsync() => api.InScopeAsync(async s =>
    {
        await s.GetRequiredService<IndexingService>().DrainAsync(Ct);
        return 0;
    });

    private static async Task<KnowledgeBaseDto> NewBaseAsync(HttpClient client, string name = "Base", string? visibility = "workspace") =>
        await Expect.JsonAsync<KnowledgeBaseDto>(await client.PostJsonAsync(ApiRoutes.Knowledge, new CreateKnowledgeBaseRequest(name, "test", null, null, null, visibility)), HttpStatusCode.Created);

    private static async Task<DocumentDto> AddAsync(HttpClient client, Guid baseId, string name, string? text, string mime = "text/markdown") =>
        await Expect.JsonAsync<DocumentDto>(await client.PostJsonAsync($"{ApiRoutes.Knowledge}/{baseId}/documents", new CreateDocumentRequest(name, mime, text?.Length ?? 1000, text)), HttpStatusCode.Created);

    private static async Task<DocumentDto> DocumentAsync(HttpClient client, Guid baseId, Guid id) =>
        (await client.GetJsonAsync<Paged<DocumentDto>>($"{ApiRoutes.Knowledge}/{baseId}/documents?pageSize=100")).Items.Single(d => d.Id == id);

    [Fact]
    public async Task Each_Role_Gets_The_Answer_Its_Permissions_Allow()
    {
        using var anonymous = api.CreateClient();
        await Expect.ProblemAsync(await anonymous.GetAsync(ApiRoutes.Knowledge, Ct), HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
        using var viewer = await ClientAsync(Roles.Viewer);
        using var member = await ClientAsync(Roles.Member);
        using var admin = await ClientAsync(Roles.Admin);
        foreach (var client in new[] { viewer, member, admin })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ApiRoutes.Knowledge, Ct)).StatusCode);
        }
        var request = new CreateKnowledgeBaseRequest("Nope", "", null, null, null, null);
        await Expect.ProblemAsync(await viewer.PostJsonAsync(ApiRoutes.Knowledge, request), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await member.PostJsonAsync(ApiRoutes.Knowledge, request), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);

        var shared = await NewBaseAsync(admin, "Shared");
        await Expect.ProblemAsync(await viewer.PostJsonAsync($"{ApiRoutes.Knowledge}/{shared.Id}/documents", new CreateDocumentRequest("a.md", "text/markdown", 5, "hello")), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await member.PatchJsonAsync($"{ApiRoutes.Knowledge}/{shared.Id}", new UpdateKnowledgeBaseRequest("x", null, null, null, null, null, null, null, null, shared.Version)), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        await Expect.ProblemAsync(await member.SendDeleteAsync($"{ApiRoutes.Knowledge}/{shared.Id}"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(HttpStatusCode.Created, (await member.PostJsonAsync($"{ApiRoutes.Knowledge}/{shared.Id}/documents", new CreateDocumentRequest("m.md", "text/markdown", 5, "member note"))).StatusCode);
    }

    [Fact]
    public async Task A_Base_Is_Validated_Versioned_And_Re_Cuts_Its_Documents_When_The_Chunking_Changes()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var fields = await Expect.ProblemAsync(await admin.PostJsonAsync(ApiRoutes.Knowledge, new CreateKnowledgeBaseRequest("", "", null, 50, 40, "secret")), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["chunkOverlap", "chunkSize", "name", "visibility"], fields.Order(StringComparer.Ordinal));

        var kb = await NewBaseAsync(admin);
        var doc = await AddAsync(admin, kb.Id, "a.md", string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"Paragraph {i} about exporting large files and how long they take.")));
        await DrainAsync();
        Assert.Equal(DocumentStates.Ready, (await DocumentAsync(admin, kb.Id, doc.Id)).State);

        var updated = await Expect.JsonAsync<KnowledgeBaseDto>(await admin.PatchJsonAsync($"{ApiRoutes.Knowledge}/{kb.Id}",
            new UpdateKnowledgeBaseRequest(null, null, 200, 20, null, null, null, null, null, kb.Version)));
        Assert.Equal((200, 1), (updated.ChunkSize, updated.InProgressCount));
        await Expect.ProblemAsync(await admin.PatchJsonAsync($"{ApiRoutes.Knowledge}/{kb.Id}",
            new UpdateKnowledgeBaseRequest("late", null, null, null, null, null, null, null, null, kb.Version)), HttpStatusCode.Conflict, ErrorCodes.Conflict);
        await DrainAsync();
        Assert.Equal(DocumentStates.Ready, (await DocumentAsync(admin, kb.Id, doc.Id)).State);
    }

    [Fact]
    public async Task A_Private_Base_Does_Not_Exist_For_Anyone_But_Its_Owner()
    {
        using var alice = await ClientAsync(Roles.Admin);
        using var owner = await ClientAsync(Roles.Owner);
        var mine = await NewBaseAsync(alice, "Mine", "private");
        var doc = await AddAsync(alice, mine.Id, "secret.md", "the launch codes are hidden in this document");
        await DrainAsync();

        var url = $"{ApiRoutes.Knowledge}/{mine.Id}";
        Assert.DoesNotContain(await owner.GetJsonAsync<IReadOnlyList<KnowledgeBaseDto>>(ApiRoutes.Knowledge), b => b.Id == mine.Id);
        await Expect.ProblemAsync(await owner.GetAsync(url + "/documents", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await owner.PostJsonAsync(url + "/search", new SearchKnowledgeRequest("launch codes", null)), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await owner.PostJsonAsync(url + "/documents", new CreateDocumentRequest("x.md", "text/markdown", 1, "x")), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await owner.PatchJsonAsync(url, new UpdateKnowledgeBaseRequest("x", null, null, null, null, null, null, null, null, 0)), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await owner.SendDeleteAsync(url), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await owner.SendDeleteAsync($"{ApiRoutes.Documents}/{doc.Id}"), HttpStatusCode.NotFound, ErrorCodes.NotFound);
        await Expect.ProblemAsync(await owner.PostAsync($"{ApiRoutes.Documents}/{doc.Id}/reindex", null, Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);

        // The chat applies the same rule: asking for the base by id cites nothing from it for the owner, and does for its author.
        async Task<int> CitationsAsync(HttpClient client)
        {
            var chat = await Expect.JsonAsync<TinaClass.Contracts.Chat.ConversationDto>(await client.PostJsonAsync(ApiRoutes.Conversations, new TinaClass.Contracts.Chat.CreateConversationRequest(null, null, null, null)), HttpStatusCode.Created);
            var events = await Sse.ReadChatAsync(await client.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/messages",
                new TinaClass.Contracts.Chat.SendMessageRequest("where are the launch codes", null, null, [mine.Id], false, null, null)));
            return events.Count(e => e.Event.Event == TinaClass.Contracts.Chat.StreamEvents.Citation);
        }
        Assert.Equal(0, await CitationsAsync(owner));
        Assert.True(await CitationsAsync(alice) > 0);
        Assert.Equal(HttpStatusCode.OK, (await alice.PostJsonAsync(url + "/search", new SearchKnowledgeRequest("launch codes", null))).StatusCode);
    }

    [Fact]
    public async Task A_Document_Moves_Through_The_States_One_Step_At_A_Time_And_Is_Then_Found()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var kb = await NewBaseAsync(admin);
        var doc = await AddAsync(admin, kb.Id, "export.md", "Large exports run in an asynchronous queue so that nobody has to wait at the page.\n\nThe quokka population is unrelated.");
        Assert.Equal((DocumentStates.Queued, 0), (doc.State, doc.Progress));
        Assert.Equal(1, (await admin.GetJsonAsync<IReadOnlyList<KnowledgeBaseDto>>(ApiRoutes.Knowledge)).Single(b => b.Id == kb.Id).InProgressCount);

        var seen = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            await api.InScopeAsync(async s => await s.GetRequiredService<IndexingService>().AdvanceAsync(Ct));
            seen.Add((await DocumentAsync(admin, kb.Id, doc.Id)).State);
            if (seen[^1] == DocumentStates.Ready)
            {
                break;
            }
        }
        Assert.Equal([DocumentStates.Uploading, DocumentStates.Parsing, DocumentStates.Chunking, DocumentStates.Embedding, DocumentStates.Ready], seen);
        var ready = await DocumentAsync(admin, kb.Id, doc.Id);
        Assert.True(ready.ChunkCount > 0);
        Assert.Equal(100, ready.Progress);
        Assert.Contains(api.Events.Events, e => e.Type == KnowledgeEvents.DocumentIndexed && e.Json.Contains(doc.Id.ToString()));

        var hits = (await Expect.JsonAsync<SearchKnowledgeResponse>(await admin.PostJsonAsync($"{ApiRoutes.Knowledge}/{kb.Id}/search", new SearchKnowledgeRequest("asynchronous queue for large exports", 3)))).Hits;
        Assert.NotEmpty(hits);
        Assert.Equal(doc.Id, hits[0].DocumentId);
        Assert.Contains("asynchronous queue", hits[0].Text);
        await Expect.ProblemAsync(await admin.PostJsonAsync($"{ApiRoutes.Knowledge}/{kb.Id}/search", new SearchKnowledgeRequest("x", 99)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        await Expect.ProblemAsync(await admin.PostJsonAsync($"{ApiRoutes.Knowledge}/{kb.Id}/search", new SearchKnowledgeRequest(" ", null)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
    }

    [Fact]
    public async Task The_Same_Content_Twice_Is_A_409_Duplicate()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var kb = await NewBaseAsync(admin);
        await AddAsync(admin, kb.Id, "one.md", "identical text");
        await Expect.ProblemAsync(await admin.PostJsonAsync($"{ApiRoutes.Knowledge}/{kb.Id}/documents", new CreateDocumentRequest("two.md", "text/markdown", 14, "identical text")), HttpStatusCode.Conflict, ErrorCodes.DuplicateDocument);
        var other = await NewBaseAsync(admin, "Other");
        await AddAsync(admin, other.Id, "one.md", "identical text");
    }

    [Fact]
    public async Task A_File_With_No_Text_Fails_With_A_Reason_And_Can_Be_Tried_Again_Only_When_It_Is_Not_Running()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var kb = await NewBaseAsync(admin);
        var picture = await AddAsync(admin, kb.Id, "flow.png", null, "image/png");
        await Expect.ProblemAsync(await admin.PostAsync($"{ApiRoutes.Documents}/{picture.Id}/reindex", null, Ct), HttpStatusCode.Conflict, ErrorCodes.InvalidState);
        await DrainAsync();
        var failed = await DocumentAsync(admin, kb.Id, picture.Id);
        Assert.Equal(DocumentStates.Failed, failed.State);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));
        Assert.Contains(api.Events.Events, e => e.Type == KnowledgeEvents.DocumentFailed && e.Json.Contains(picture.Id.ToString()));

        var again = await Expect.JsonAsync<DocumentDto>(await admin.PostAsync($"{ApiRoutes.Documents}/{picture.Id}/reindex", null, Ct));
        Assert.Equal(DocumentStates.Reindexing, again.State);
        await DrainAsync();
        Assert.Equal(DocumentStates.Failed, (await DocumentAsync(admin, kb.Id, picture.Id)).State);
    }

    [Fact]
    public async Task A_Document_Can_Be_Deleted_By_Its_Uploader_Or_A_Manager_And_Its_Text_Stops_Being_Found()
    {
        using var admin = await ClientAsync(Roles.Admin);
        using var alice = await ClientAsync(Roles.Member);
        using var bob = await ClientAsync(Roles.Member);
        var kb = await NewBaseAsync(admin);
        var doc = await AddAsync(alice, kb.Id, "alice.md", "zebras graze at dawn near the river");
        await DrainAsync();
        Assert.NotEmpty((await Expect.JsonAsync<SearchKnowledgeResponse>(await bob.PostJsonAsync($"{ApiRoutes.Knowledge}/{kb.Id}/search", new SearchKnowledgeRequest("zebras graze", null)))).Hits);

        await Expect.ProblemAsync(await bob.SendDeleteAsync($"{ApiRoutes.Documents}/{doc.Id}"), HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.SendDeleteAsync($"{ApiRoutes.Documents}/{doc.Id}")).StatusCode);
        Assert.Empty((await Expect.JsonAsync<SearchKnowledgeResponse>(await bob.PostJsonAsync($"{ApiRoutes.Knowledge}/{kb.Id}/search", new SearchKnowledgeRequest("zebras graze", null)))).Hits);
        await Expect.ProblemAsync(await alice.SendDeleteAsync($"{ApiRoutes.Documents}/{doc.Id}"), HttpStatusCode.NotFound, ErrorCodes.NotFound);

        var second = await AddAsync(alice, kb.Id, "second.md", "another note");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendDeleteAsync($"{ApiRoutes.Documents}/{second.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendDeleteAsync($"{ApiRoutes.Knowledge}/{kb.Id}")).StatusCode);
        Assert.DoesNotContain(await admin.GetJsonAsync<IReadOnlyList<KnowledgeBaseDto>>(ApiRoutes.Knowledge), b => b.Id == kb.Id);
    }

    [Fact]
    public async Task A_Document_Is_Checked_Before_It_Is_Stored()
    {
        using var admin = await ClientAsync(Roles.Admin);
        var kb = await NewBaseAsync(admin);
        var fields = await Expect.ProblemAsync(await admin.PostJsonAsync($"{ApiRoutes.Knowledge}/{kb.Id}/documents", new CreateDocumentRequest("", "", 60_000_000, null)), HttpStatusCode.BadRequest, ErrorCodes.Validation);
        Assert.Equal(["mime", "name", "size"], fields.Order(StringComparer.Ordinal));
        await Expect.ProblemAsync(await admin.PostJsonAsync($"{ApiRoutes.Knowledge}/{Guid.NewGuid()}/documents", new CreateDocumentRequest("a", "text/plain", 1, "a")), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }
}
