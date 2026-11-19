using System.Runtime.CompilerServices;
using TinaClass.App.Common.Sample;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Sample;

namespace TinaClass.App.Features.Chat;

/// <summary>
/// Chat on the sample data (栖云科技, <c>Contracts/Sample</c>): the conversations and their messages live in memory, and the answer is
/// a scripted stream with the same events the API sends: reasoning, a plan, two tool calls, three citations and the Markdown answer,
/// piece by piece. A question that is not about the export revamp gets an echo of what was written (what the Echo provider of the
/// API does). <see cref="Speed"/> scales every pause; tests set it to zero.
/// </summary>
public sealed class SampleChatService : IChatService
{
    /// <summary>1 = the pace of a person reading; 0 = no pauses at all (tests).</summary>
    public static double Speed { get; set; } = 1;

    /// <summary>
    /// A test seam for screenshots of a state in the middle of an answer: <c>"thinking"</c> stops after the reasoning text (before it ends), <c>"tools"</c> after the second
    /// tool call has started (before its result). The answer then waits until it is stopped or cancelled. Null (the default) lets every answer run to its end.
    /// </summary>
    public static string? HoldAt { get; set; }

    private readonly List<ConversationDto> _conversations;
    private readonly List<FolderDto> _folders = [.. SampleCore.Folders];
    private readonly Dictionary<Guid, List<MessageDto>> _messages = [];
    private readonly HashSet<Guid> _stopped = [];

    public SampleChatService()
    {
        _conversations = [.. SampleCore.Conversations.Select(c => new ConversationDto(c.Id, c.OwnerId, c.Title, c.FolderId, SpaceOf(c.FolderId), c.Pinned, false, SampleCore.DefaultModel.Code,
            null, 0, 0, SampleClock.At(c.DaysAgo, c.Hour, c.Minute), 1))];
        foreach (var c in _conversations)
        {
            var messages = SampleChat.Messages(c.Id, SampleClock.Now).ToList();
            _messages[c.Id] = messages;
        }
        for (var i = 0; i < _conversations.Count; i++)
        {
            _conversations[i] = _conversations[i] with { ActiveLeafId = _messages[_conversations[i].Id].LastOrDefault()?.Id };
        }
    }

    private static Guid? SpaceOf(Guid? folderId)
    {
        var folder = SampleCore.Folders.FirstOrDefault(f => f.Id == folderId);
        return SampleCore.Spaces.FirstOrDefault(s => s.Name == folder?.Name)?.Id;
    }

    public Task<IReadOnlyList<FolderDto>> FoldersAsync(CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<FolderDto>>([.. _folders.Select(f => f with { ConversationCount = _conversations.Count(c => c.FolderId == f.Id && !c.Archived) })]);

    public Task<IReadOnlyList<SpaceDto>> SpacesAsync(CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<SpaceDto>>([.. SampleCore.Spaces.Select(s => s with { ConversationCount = _conversations.Count(c => c.SpaceId == s.Id && !c.Archived) })]);

    public Task<IReadOnlyList<ConversationDto>> ConversationsAsync(CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<ConversationDto>>([.. _conversations.Where(c => !c.Archived).OrderByDescending(c => c.Pinned).ThenByDescending(c => c.UpdatedAt)]);

    public Task<ConversationDto> CreateAsync(string? title, Guid? folderId, Guid? spaceId, string? modelCode, CancellationToken cancellation = default)
    {
        var conversation = new ConversationDto(Guid.NewGuid(), SampleIds.Owner, title ?? string.Empty, folderId, spaceId ?? SpaceOf(folderId), false, false, modelCode ?? SampleCore.DefaultModel.Code,
            null, 0, 0, SampleClock.Now, 1);
        _conversations.Add(conversation);
        _messages[conversation.Id] = [];
        return Task.FromResult(conversation);
    }

    public Task<IReadOnlyList<MessageDto>> MessagesAsync(Guid conversationId, CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<MessageDto>>(_messages.TryGetValue(conversationId, out var list) ? [.. list] : []);

    public async IAsyncEnumerable<ChatStreamEvent> SendAsync(Guid conversationId, SendMessageRequest request, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var list = _messages[conversationId];
        var parent = request.ParentId ?? list.LastOrDefault(m => m.Role == MessageRoles.Assistant)?.Id;
        var user = new MessageDto(Guid.NewGuid(), conversationId, parent, null, MessageRoles.User, [new MessageBlock("text", request.Text)], MessageStatuses.Done, null, null, null, null,
            SampleClock.Now, 0, 1, []);
        list.Add(user);
        var index = _conversations.FindIndex(c => c.Id == conversationId);
        if (string.IsNullOrWhiteSpace(_conversations[index].Title))
        {
            _conversations[index] = _conversations[index] with { Title = TitleFor(request.Text) };
        }
        await foreach (var e in AnswerAsync(conversationId, user, request.ModelCode, request.Text, 0, 1, cancellation))
        {
            yield return e;
        }
    }

    public async IAsyncEnumerable<ChatStreamEvent> RegenerateAsync(Guid messageId, string? modelCode, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var (conversationId, list) = _messages.First(kv => kv.Value.Any(m => m.Id == messageId));
        var old = list.First(m => m.Id == messageId);
        var user = list.First(m => m.Id == old.ParentId);
        var count = list.Count(m => m.ParentId == user.Id && m.Role == MessageRoles.Assistant) + 1;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].ParentId == user.Id && list[i].Role == MessageRoles.Assistant)
            {
                list[i] = list[i] with { SiblingCount = count };
            }
        }
        await foreach (var e in AnswerAsync(conversationId, user, modelCode, Text(user), count - 1, count, cancellation))
        {
            yield return e;
        }
    }

    public Task StopAsync(Guid messageId, CancellationToken cancellation = default)
    {
        _stopped.Add(messageId);
        return Task.CompletedTask;
    }

    public Task FeedbackAsync(Guid messageId, string value, CancellationToken cancellation = default)
    {
        foreach (var list in _messages.Values)
        {
            var i = list.FindIndex(m => m.Id == messageId);
            if (i >= 0)
            {
                list[i] = list[i] with { Feedback = value.Length == 0 ? null : value };
            }
        }
        return Task.CompletedTask;
    }

    public Task<ConversationDto> UpdateAsync(ConversationDto conversation, string? title = null, Guid? folderId = null, bool moveToRoot = false, bool? pinned = null,
        bool? archived = null, CancellationToken cancellation = default)
    {
        var i = _conversations.FindIndex(c => c.Id == conversation.Id);
        var current = _conversations[i];
        var folder = moveToRoot ? null : folderId ?? current.FolderId;
        _conversations[i] = current with { Title = title ?? current.Title, FolderId = folder, SpaceId = SpaceOf(folder), Pinned = pinned ?? current.Pinned, Archived = archived ?? current.Archived, Version = current.Version + 1 };
        return Task.FromResult(_conversations[i]);
    }

    public Task DeleteAsync(Guid conversationId, CancellationToken cancellation = default)
    {
        _conversations.RemoveAll(c => c.Id == conversationId);
        _messages.Remove(conversationId);
        return Task.CompletedTask;
    }

    public Task<FolderDto> CreateFolderAsync(string name, CancellationToken cancellation = default)
    {
        var folder = new FolderDto(Guid.NewGuid(), null, name, _folders.Count + 1, 0);
        _folders.Add(folder);
        return Task.FromResult(folder);
    }

    public Task<IReadOnlyList<SearchResultDto>> SearchAsync(string query, CancellationToken cancellation = default) =>
        Task.FromResult<IReadOnlyList<SearchResultDto>>([.. _conversations.Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8)
            .Select(c => new SearchResultDto("conversation", c.Id, c.Title, string.Empty))]);

    /// <summary>A title for a new conversation: the first words of its first message.</summary>
    public static string TitleFor(string text)
    {
        var line = text.Trim().ReplaceLineEndings(" ");
        return line.Length <= 24 ? line : line[..24] + "…";
    }

    private static string Text(MessageDto message) => message.Blocks.FirstOrDefault(b => b.Kind == "text")?.Text ?? string.Empty;

    private static bool IsAboutExport(string text) =>
        text.Contains("导出") || text.Contains("痛点") || text.Contains("优先级") || text.Contains("export", StringComparison.OrdinalIgnoreCase) || text.Contains("priorit", StringComparison.OrdinalIgnoreCase);

    private static Task Hold(string phase, CancellationToken cancellation) => HoldAt == phase ? Task.Delay(Timeout.Infinite, cancellation) : Task.CompletedTask;

    private static Task Pause(int ms, CancellationToken cancellation) => Speed <= 0 ? Task.CompletedTask : Task.Delay((int)(ms * Speed), cancellation);

    private async IAsyncEnumerable<ChatStreamEvent> AnswerAsync(Guid conversationId, MessageDto user, string? modelCode, string text, int siblingIndex, int siblingCount,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        var answerId = Guid.NewGuid();
        var model = modelCode ?? SampleCore.DefaultModel.Code;
        var story = IsAboutExport(text) && model != "echo-1";
        var blocks = new List<MessageBlock>();
        var body = new System.Text.StringBuilder();
        var status = MessageStatuses.Done;
        yield return new ChatStreamEvent(StreamEvents.MessageStart, MessageId: answerId, UserMessageId: user.Id, Model: model);

        if (story)
        {
            await Pause(300, cancellation);
            foreach (var piece in Pieces(SampleChat.Reasoning))
            {
                await Pause(22, cancellation);
                yield return new ChatStreamEvent(StreamEvents.ReasoningDelta, MessageId: answerId, Text: piece);
            }
            await Hold("thinking", cancellation);
            yield return new ChatStreamEvent(StreamEvents.ReasoningEnd, MessageId: answerId, Ms: 6_000);
            blocks.Add(new MessageBlock("reasoning", SampleChat.Reasoning, Ms: 6_000));
            yield return new ChatStreamEvent(StreamEvents.Plan, MessageId: answerId, Steps: SampleChat.Plan("pending"));
            var tools = SampleChat.ToolBlocks();
            for (var i = 0; i < tools.Length; i++)
            {
                var t = tools[i];
                await Pause(420, cancellation);
                yield return new ChatStreamEvent(StreamEvents.Plan, MessageId: answerId, Steps: Steps(i, running: true));
                yield return new ChatStreamEvent(StreamEvents.ToolStart, MessageId: answerId, ToolId: t.ToolId, Name: t.Title, Icon: t.Icon, Arguments: t.Arguments, State: "running");
                if (i == 1)
                {
                    await Hold("tools", cancellation);
                }
                await Pause(700, cancellation);
                yield return new ChatStreamEvent(StreamEvents.ToolResult, MessageId: answerId, ToolId: t.ToolId, Result: t.Result, Summary: t.Summary, State: "done", Ms: t.Ms);
                yield return new ChatStreamEvent(StreamEvents.Plan, MessageId: answerId, Steps: Steps(i, running: false));
            }
            blocks.Add(new MessageBlock("plan", Steps: SampleChat.Plan()));
            blocks.AddRange(tools);
            foreach (var s in SampleChat.Sources)
            {
                await Pause(120, cancellation);
                yield return new ChatStreamEvent(StreamEvents.Citation, MessageId: answerId, Position: s.Position, DocumentId: SampleIds.Document(s.DocumentNo), ChunkId: SampleIds.Chunk(s.ChunkNo),
                    Score: s.Score, Title: s.Title, Quote: s.Quote, Summary: s.Where);
            }
            yield return new ChatStreamEvent(StreamEvents.Plan, MessageId: answerId, Steps: SampleChat.Plan());
        }
        else
        {
            await Pause(250, cancellation);
            var thought = SampleClock.Pick("Echo provider: no model is behind this answer, so it repeats the question back.", "Echo 示例：没有真实模型，回答会把问题复述一遍。", "Echo 示例：沒有真實模型，回答會把問題複述一遍。");
            foreach (var piece in Pieces(thought))
            {
                await Pause(18, cancellation);
                yield return new ChatStreamEvent(StreamEvents.ReasoningDelta, MessageId: answerId, Text: piece);
            }
            yield return new ChatStreamEvent(StreamEvents.ReasoningEnd, MessageId: answerId, Ms: 1_200);
            blocks.Add(new MessageBlock("reasoning", thought, Ms: 1_200));
        }

        var answer = story ? SampleChat.Answer : SampleClock.Pick($"You said: \"{text}\"\n\nThis is the built-in Echo answer: add a provider key under Models to get a real one.",
            $"你说：「{text}」\n\n这是内置 Echo 示例的回答。在“模型”页添加服务商密钥后，这里会换成真实模型的回答。",
            $"你說：「{text}」\n\n這是內建 Echo 示例的回答。在「模型」頁新增服務商金鑰後，這裡會換成真實模型的回答。");
        foreach (var piece in Pieces(answer))
        {
            if (cancellation.IsCancellationRequested || _stopped.Remove(answerId))
            {
                status = MessageStatuses.Stopped;
                break;
            }
            await Pause(story ? 24 : 18, cancellation);
            body.Append(piece);
            yield return new ChatStreamEvent(StreamEvents.TextDelta, MessageId: answerId, Text: piece);
        }
        blocks.Add(new MessageBlock("text", body.ToString()));
        var usage = story ? SampleChat.Usage : new MessageUsageDto(Math.Max(20, text.Length * 2), Math.Max(20, body.Length / 2), 0, 120, 1_800, 0, true);
        yield return new ChatStreamEvent(StreamEvents.Usage, MessageId: answerId, Usage: usage);
        _messages[conversationId].Add(new MessageDto(answerId, conversationId, user.Id, null, MessageRoles.Assistant, blocks, status, null, model, usage, null, SampleClock.Now, siblingIndex, siblingCount,
            story ? SampleChat.Citations(answerId) : []));
        var i2 = _conversations.FindIndex(c => c.Id == conversationId);
        _conversations[i2] = _conversations[i2] with { ActiveLeafId = answerId, UpdatedAt = SampleClock.Now, TokenTotal = _conversations[i2].TokenTotal + usage.TokensIn + usage.TokensOut };
        yield return new ChatStreamEvent(StreamEvents.MessageDone, MessageId: answerId, Status: status);
    }

    private static IReadOnlyList<PlanStepDto> Steps(int tool, bool running)
    {
        // Steps 1 and 2 are the two tools; 3 and 4 follow them.
        var plan = SampleChat.Plan("pending");
        return [.. plan.Select((s, n) => s with { State = n < tool ? "done" : n == tool ? (running ? "running" : "done") : "pending" })];
    }

    /// <summary>The text in pieces of a few words (or a few Chinese characters), so that it appears the way a model streams it.</summary>
    public static IEnumerable<string> Pieces(string text)
    {
        var start = 0;
        var units = 0;
        for (var i = 0; i < text.Length; i++)
        {
            units += text[i] > 0x2E80 ? 1 : text[i] == ' ' ? 2 : 0;
            if (units >= 6 || text[i] == '\n')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
                units = 0;
            }
        }
        if (start < text.Length)
        {
            yield return text[start..];
        }
    }
}
