using Avalonia.Headless.XUnit;
using TinaClass.App.Features.Chat;
using TinaClass.Contracts.Chat;
using Xunit;

namespace TinaClass.App.Tests.Features.Chat;

/// <summary>The sample chat service: the stream follows the contract's event order, an answer can be stopped and regenerated, conversations can be changed.</summary>
public class ChatServiceTests
{
    private const string ExportQuestion = "帮我梳理一下「导出功能」的用户痛点，并给出优先级建议";

    private static async Task<(Guid Conversation, List<ChatStreamEvent> Events)> AskAsync(IChatService chat, string text, string? model = null)
    {
        var conversation = await chat.CreateAsync(null, null, null, model);
        var events = new List<ChatStreamEvent>();
        await foreach (var e in chat.SendAsync(conversation.Id, new SendMessageRequest(text, null, model, null, false, null, null)))
        {
            events.Add(e);
        }
        return (conversation.Id, events);
    }

    [AvaloniaFact]
    public async Task An_answer_streams_in_the_order_of_the_contract()
    {
        TestApp.Prepare();
        var (_, events) = await AskAsync(Ui.Service<IChatService>(), ExportQuestion);
        var names = events.Select(e => e.Event).ToList();

        Assert.Equal(StreamEvents.MessageStart, names[0]);
        Assert.Equal(StreamEvents.MessageDone, names[^1]);
        // reasoning, plan, tool calls, citations, text, usage: each group begins after the one before it.
        string[] order = [StreamEvents.ReasoningDelta, StreamEvents.ReasoningEnd, StreamEvents.Plan, StreamEvents.ToolStart, StreamEvents.ToolResult, StreamEvents.Citation, StreamEvents.TextDelta, StreamEvents.Usage];
        var firsts = order.Select(name => names.IndexOf(name)).ToList();
        Assert.DoesNotContain(-1, firsts);
        Assert.Equal(firsts.OrderBy(i => i), firsts);
        Assert.Equal(2, names.Count(n => n == StreamEvents.ToolStart));
        Assert.Equal(3, names.Count(n => n == StreamEvents.Citation));
        Assert.Equal(MessageStatuses.Done, events[^1].Status);
    }

    [AvaloniaFact]
    public async Task The_streamed_text_is_the_saved_answer()
    {
        TestApp.Prepare();
        var chat = Ui.Service<IChatService>();
        var (conversation, events) = await AskAsync(chat, ExportQuestion);
        var streamed = string.Concat(events.Where(e => e.Event == StreamEvents.TextDelta).Select(e => e.Text));
        var messages = await chat.MessagesAsync(conversation);

        var answer = messages.Single(m => m.Role == MessageRoles.Assistant);
        Assert.Equal(streamed, answer.Blocks.Single(b => b.Kind == "text").Text);
        Assert.Equal(3, answer.Citations.Count);
        Assert.Equal(MessageStatuses.Done, answer.Status);
    }

    [AvaloniaFact]
    public async Task Another_question_gets_the_echo_answer_without_a_made_up_cost()
    {
        TestApp.Prepare();
        var (_, events) = await AskAsync(Ui.Service<IChatService>(), "hello there", "echo-1");
        var text = string.Concat(events.Where(e => e.Event == StreamEvents.TextDelta).Select(e => e.Text));

        Assert.Contains("hello there", text);
        Assert.DoesNotContain(events, e => e.Event is StreamEvents.ToolStart or StreamEvents.Citation);
        var usage = events.Single(e => e.Event == StreamEvents.Usage).Usage!;
        Assert.Equal(0, usage.Cost);
        Assert.True(usage.Estimated);
    }

    [AvaloniaFact]
    public async Task Stopping_keeps_what_was_generated()
    {
        TestApp.Prepare();
        var chat = Ui.Service<IChatService>();
        var conversation = await chat.CreateAsync(null, null, null, null);
        var events = new List<ChatStreamEvent>();
        await foreach (var e in chat.SendAsync(conversation.Id, new SendMessageRequest(ExportQuestion, null, null, null, false, null, null)))
        {
            events.Add(e);
            if (e.Event == StreamEvents.TextDelta && events.Count(x => x.Event == StreamEvents.TextDelta) == 3)
            {
                await chat.StopAsync(e.MessageId!.Value);
            }
        }

        Assert.Equal(MessageStatuses.Stopped, events[^1].Status);
        var answer = (await chat.MessagesAsync(conversation.Id)).Single(m => m.Role == MessageRoles.Assistant);
        Assert.Equal(MessageStatuses.Stopped, answer.Status);
        var kept = answer.Blocks.Single(b => b.Kind == "text").Text!;
        Assert.NotEmpty(kept);
        Assert.True(kept.Length < SampleChatFull.Length, "only the part that was generated is kept");
    }

    [AvaloniaFact]
    public async Task Regenerating_adds_a_sibling_and_the_shown_branch_follows()
    {
        TestApp.Prepare();
        var chat = Ui.Service<IChatService>();
        var (conversation, events) = await AskAsync(chat, ExportQuestion);
        var first = events[0].MessageId!.Value;

        await foreach (var _ in chat.RegenerateAsync(first, null))
        {
        }
        var answers = (await chat.MessagesAsync(conversation)).Where(m => m.Role == MessageRoles.Assistant).ToList();

        // The shown branch has the newest answer; it knows it is one of two.
        var shown = answers[^1];
        Assert.NotEqual(first, shown.Id);
        Assert.Equal(2, shown.SiblingCount);
        Assert.Equal(1, shown.SiblingIndex);
    }

    [AvaloniaFact]
    public async Task Conversations_can_be_renamed_pinned_moved_and_deleted()
    {
        TestApp.Prepare();
        var hub = Ui.Service<ChatHub>();
        await hub.RefreshAsync();
        var target = hub.Conversations.First(c => !c.Pinned);

        await hub.RenameAsync(target.Id, "Renamed");
        Assert.Equal("Renamed", hub.Find(target.Id)!.Title);

        await hub.PinAsync(target.Id, true);
        Assert.True(hub.Find(target.Id)!.Pinned);
        Assert.Contains(hub.Conversations.Where(c => c.Pinned), c => c.Id == target.Id);

        var folder = await hub.CreateFolderAsync("New folder");
        await hub.MoveAsync(target.Id, folder.Id);
        Assert.Equal(folder.Id, hub.Find(target.Id)!.FolderId);

        await hub.DeleteAsync(target.Id);
        Assert.Null(hub.Find(target.Id));
    }

    [AvaloniaFact]
    public void A_title_is_made_from_the_first_words()
    {
        Assert.Equal("short", SampleChatService.TitleFor("short"));
        var title = SampleChatService.TitleFor(new string('x', 60));
        Assert.Equal(25, title.Length);
        Assert.EndsWith("…", title);
    }

    private static string SampleChatFull => TinaClass.Contracts.Sample.SampleChat.Answer;
}
