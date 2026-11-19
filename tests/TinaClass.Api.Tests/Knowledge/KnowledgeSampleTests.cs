using System.Net;
using TinaClass.Api.Tests.Infrastructure;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Knowledge;
using TinaClass.Contracts.Sample;

namespace TinaClass.Api.Tests.Knowledge;

/// <summary>Retrieval and citations on the sample workspace, which has its own database.</summary>
public sealed class KnowledgeSampleTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> OwnerAsync()
    {
        await api.SeedSampleAsync();
        return await api.SignInAsync(SampleCore.People[0].Email);
    }

    [Fact]
    public async Task The_Sample_Chunks_Are_Found_By_A_Question_In_Words_Not_Copied_From_Them()
    {
        using var owner = await OwnerAsync();
        var hits = (await Expect.JsonAsync<SearchKnowledgeResponse>(await owner.PostJsonAsync($"{ApiRoutes.Knowledge}/{SampleIds.Base(1)}/search", new SearchKnowledgeRequest("导出等待时间太长", 5)))).Hits;
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.InRange(h.Score, 0, 1.0001));
        Assert.Equal(hits.Select(h => h.Score).OrderDescending(), hits.Select(h => h.Score));
        Assert.Contains(hits, h => h.DocumentName.Contains("调研") || h.DocumentName.Contains("技术方案"));
    }

    [Fact]
    public async Task The_Sample_Documents_Tell_Every_State()
    {
        using var owner = await OwnerAsync();
        var documents = (await owner.GetJsonAsync<Paged<DocumentDto>>($"{ApiRoutes.Knowledge}/{SampleIds.Base(1)}/documents?pageSize=100")).Items;
        foreach (var state in new[] { DocumentStates.Ready, DocumentStates.Embedding, DocumentStates.Parsing, DocumentStates.Queued, DocumentStates.Failed })
        {
            Assert.Contains(documents, d => d.State == state);
        }
        Assert.Equal("图片里没有可识别的文字", documents.Single(d => d.State == DocumentStates.Failed).Error);
    }

    [Fact]
    public async Task An_Answer_With_A_Base_Cites_Its_Sources_And_A_Citation_Opens_The_Passage()
    {
        using var owner = await OwnerAsync();
        var chat = await Expect.JsonAsync<ConversationDto>(await owner.PostJsonAsync(ApiRoutes.Conversations, new CreateConversationRequest(null, null, null, null)), HttpStatusCode.Created);
        var response = await owner.PostJsonAsync($"{ApiRoutes.Conversations}/{chat.Id}/messages",
            new SendMessageRequest("导出等待时间为什么这么长", null, null, [SampleIds.Base(1)], false, null, null));
        var events = await Sse.ReadChatAsync(response);
        var names = events.Select(e => e.Event.Event).ToList();
        Assert.Contains(StreamEvents.ToolStart, names);
        Assert.Contains(StreamEvents.Citation, names);
        Assert.True(names.IndexOf(StreamEvents.Citation) < names.IndexOf(StreamEvents.TextDelta) || names.Contains(StreamEvents.TextDelta));

        var answer = (await owner.GetJsonAsync<IReadOnlyList<MessageDto>>($"{ApiRoutes.Conversations}/{chat.Id}/messages")).Last();
        Assert.NotEmpty(answer.Citations);
        var citation = answer.Citations[0];
        var detail = await owner.GetJsonAsync<CitationDetailDto>($"{ApiRoutes.Citations}/{citation.Id}");
        Assert.Equal(citation.DocumentName, detail.DocumentName);
        Assert.False(string.IsNullOrWhiteSpace(detail.Quote));

        // Another person cannot open the citation of a conversation that is not theirs.
        using var other = await api.SignInAsync(SampleCore.People[2].Email);
        await Expect.ProblemAsync(await other.GetAsync($"{ApiRoutes.Citations}/{citation.Id}", Ct), HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }
}
