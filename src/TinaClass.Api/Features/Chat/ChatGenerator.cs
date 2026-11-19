using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Chat.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Features.Chat;

/// <summary>
/// One answer being written. It keeps every event so that a client that lost its connection can ask again from the last event it saw
/// (<c>Last-Event-ID</c>), and it keeps going when the client leaves: only <c>stop</c> ends it early.
/// </summary>
internal sealed class Generation(Guid messageId, Guid conversationId, Guid ownerId)
{
    private readonly object _gate = new();
    private readonly List<ChatStreamEvent> _events = [];
    private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Guid MessageId { get; } = messageId;

    public Guid ConversationId { get; } = conversationId;

    public Guid OwnerId { get; } = ownerId;

    public CancellationTokenSource Cancel { get; } = new();

    public bool Completed { get; private set; }

    public void Add(ChatStreamEvent e)
    {
        lock (_gate)
        {
            _events.Add(e);
            Wake();
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            Completed = true;
            Wake();
        }
    }

    private void Wake()
    {
        var old = _signal;
        _signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        old.TrySetResult();
    }

    /// <summary>The events from sequence number <paramref name="from"/> on, as they happen, until the answer is complete.</summary>
    public async IAsyncEnumerable<(int Id, ChatStreamEvent Event)> ReadAsync(int from, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var next = from;
        while (true)
        {
            ChatStreamEvent? e = null;
            Task wait;
            lock (_gate)
            {
                if (next < _events.Count)
                {
                    e = _events[next];
                }
                else if (Completed)
                {
                    yield break;
                }
                wait = _signal.Task;
            }
            if (e is not null)
            {
                yield return (next++, e);
            }
            else
            {
                await wait.WaitAsync(cancellationToken);
            }
        }
    }
}

public sealed class GenerationRegistry
{
    private readonly ConcurrentDictionary<Guid, Generation> _byMessage = new();

    internal void Add(Generation g) => _byMessage[g.MessageId] = g;

    internal Generation? Find(Guid messageId) => _byMessage.GetValueOrDefault(messageId);

    internal bool IsBusy(Guid conversationId) => _byMessage.Values.Any(g => g.ConversationId == conversationId && !g.Completed);

    internal void Remove(Guid messageId) => _byMessage.TryRemove(messageId, out _);
}

/// <summary>
/// Writes an answer (design 4.1). The request that asks for it only prepares it (checks, the two messages, the context) and returns the
/// <see cref="Generation"/>; the writing runs on its own, in its own scope, so that a closed browser tab does not kill it.
/// </summary>
internal sealed partial class ChatGenerator(ChatDbContext db, ChatService chat, IActorContext actors, IModelCatalog models, IPromptResolver prompts, IBudgetGuard budget, GenerationRegistry registry,
    IServiceScopeFactory scopes, TimeProvider time)
{
    public const int MaxText = 20_000;
    public static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(2);

    public async Task<Generation> SendAsync(Guid conversationId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        var conversation = await chat.FindAsync(conversationId, ownerOnly: true, cancellationToken);
        var validator = new Validator().Text("text", request.Text, MaxText);
        if (request.Effort is not (null or "deep" or "balanced" or "fast"))
        {
            validator.Add("effort", "Use deep, balanced or fast.");
        }
        validator.ThrowIfInvalid();
        var parentId = request.ParentId ?? conversation.ActiveLeafId;
        if (parentId is { } p && !await db.Messages.AnyAsync(m => m.Id == p && m.ConversationId == conversationId, cancellationToken))
        {
            throw ApiException.Invalid("parentId", "The message is not part of this conversation.");
        }
        var (model, actorId) = await PrepareAsync(conversation, request.ModelCode, cancellationToken);
        var text = await prompts.ResolveAsync(request.Text.Trim(), cancellationToken);
        var now = time.GetUtcNow();
        var user = new ChatMessage { Id = Guid.NewGuid(), ConversationId = conversationId, ParentId = parentId, Role = MessageRoles.User, Status = MessageStatuses.Done, CreatedAt = now,
            ContentJson = ChatMapping.ToJson([new MessageBlock("text", text)]), SearchText = text };
        var assistant = Placeholder(conversationId, user.Id, model, now.AddMilliseconds(1));
        db.Messages.AddRange(user, assistant);
        var parameters = new GenerationParams(request.KnowledgeBaseIds, request.ToolIds, request.Web, request.Effort);
        db.ExpectVersion(conversation, conversation.Version);
        conversation.ActiveLeafId = assistant.Id;
        conversation.ModelCode = model.Code;
        conversation.ParamsJson = ChatMapping.ToJson(parameters);
        conversation.UpdatedAt = now;
        if (!conversation.TitleSetByUser && (conversation.Title == ChatService.NewTitle || parentId is null))
        {
            conversation.Title = TitleFrom(text);
        }
        await db.SaveChangesAsync(cancellationToken);
        var history = await HistoryAsync(conversationId, parentId, cancellationToken);
        history.Add(new ModelMessage(MessageRoles.User, text));
        return Launch(conversation, assistant, user.Id, history, model, parameters, text, actorId);
    }

    public async Task<Generation> RegenerateAsync(Guid messageId, RegenerateRequest request, CancellationToken cancellationToken)
    {
        var original = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken) ?? throw ApiException.NotFound("The message");
        var conversation = await chat.FindAsync(original.ConversationId, ownerOnly: true, cancellationToken);
        if (original.Role != MessageRoles.Assistant || original.ParentId is null)
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "Only an answer can be written again.");
        }
        var (model, actorId) = await PrepareAsync(conversation, request.ModelCode ?? original.ModelCode, cancellationToken);
        var parameters = ChatMapping.Params(conversation.ParamsJson) ?? new GenerationParams(null, null, false, null);
        var now = time.GetUtcNow();
        var assistant = Placeholder(conversation.Id, original.ParentId, model, now);
        db.Messages.Add(assistant);
        db.ExpectVersion(conversation, conversation.Version);
        conversation.ActiveLeafId = assistant.Id;
        conversation.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        var history = await HistoryAsync(conversation.Id, original.ParentId, cancellationToken);
        var question = history.LastOrDefault(m => m.Role == MessageRoles.User)?.Content ?? "";
        return Launch(conversation, assistant, null, history, model, parameters, question, actorId);
    }

    private async Task<(ResolvedModel Model, Guid? ActorId)> PrepareAsync(Conversation conversation, string? modelCode, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        if (registry.IsBusy(conversation.Id))
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "An answer is still being written. Stop it first.");
        }
        await budget.EnsureAsync(actor.Id, cancellationToken);
        var model = await models.ResolveAsync(modelCode ?? conversation.ModelCode, cancellationToken)
                    ?? await models.ResolveAsync(null, cancellationToken)
                    ?? throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.NoModel, "No model is enabled. Add or enable one on the Models page.");
        return (model, actor.Id);
    }

    private ChatMessage Placeholder(Guid conversationId, Guid? parentId, ResolvedModel model, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), ConversationId = conversationId, ParentId = parentId, Role = MessageRoles.Assistant, Status = MessageStatuses.Queued, ModelCode = model.Code, CreatedAt = at, ContentJson = "[]" };

    private async Task<List<ModelMessage>> HistoryAsync(Guid conversationId, Guid? leaf, CancellationToken cancellationToken)
    {
        var all = await db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId).ToDictionaryAsync(m => m.Id, cancellationToken);
        var path = new List<ModelMessage>();
        for (var node = leaf is { } l && all.TryGetValue(l, out var f) ? f : null; node is not null; node = node.ParentId is { } p && all.TryGetValue(p, out var parent) ? parent : null)
        {
            var text = ChatMapping.PlainText(ChatMapping.Blocks(node.ContentJson));
            if (text.Length > 0)
            {
                path.Add(new ModelMessage(node.Role, text));
            }
        }
        path.Reverse();
        var space = await (from c in db.Conversations.AsNoTracking() join s in db.Spaces.AsNoTracking() on c.SpaceId equals s.Id where c.Id == conversationId select s.Instruction).FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(space))
        {
            path.Insert(0, new ModelMessage("system", space));
        }
        return path;
    }

    private static string TitleFrom(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 30 ? line : line[..30].TrimEnd() + "…";
    }

    private Generation Launch(Conversation conversation, ChatMessage assistant, Guid? userMessageId, List<ModelMessage> history, ResolvedModel model, GenerationParams parameters, string question, Guid? actorId)
    {
        var generation = new Generation(assistant.Id, conversation.Id, conversation.OwnerId);
        registry.Add(generation);
        var run = new GenerationRun(generation, userMessageId, history, model, parameters, question, actorId);
        _ = Task.Run(() => RunAsync(run));
        return generation;
    }

    private sealed record GenerationRun(Generation Generation, Guid? UserMessageId, List<ModelMessage> History, ResolvedModel Model, GenerationParams Parameters, string Question, Guid? ActorId);

    private async Task RunAsync(GenerationRun run)
    {
        var generation = run.Generation;
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var messageId = generation.MessageId;
        var token = generation.Cancel.Token;
        var clock = Stopwatch.StartNew();
        var reasoning = new System.Text.StringBuilder();
        int? reasoningMs = null;
        List<PlanStepDto>? plan = null;
        var tools = new List<MessageBlock>();
        var citations = new List<ChatCitation>();
        var text = new System.Text.StringBuilder();
        MessageUsageDto? usage = null;
        var status = MessageStatuses.Done;
        string? error = null;
        try
        {
            generation.Add(new ChatStreamEvent(StreamEvents.MessageStart, MessageId: messageId, UserMessageId: run.UserMessageId, Model: run.Model.Code));
            var retriever = sp.GetRequiredService<IKnowledgeRetriever>();
            IReadOnlyList<RetrievedChunk> sources = run.Parameters.KnowledgeBaseIds is { Count: > 0 } bases ? await retriever.SearchAsync(run.Question, bases, 4, token) : [];
            var catalog = await sp.GetRequiredService<IToolCatalog>().ListAsync(token);
            var chosen = run.Parameters.ToolIds is { Count: > 0 } ids ? catalog.Where(t => ids.Contains(t.Id.ToString("N")) || ids.Contains(t.Id.ToString())).ToList() : [];
            var gateway = sp.GetRequiredService<IModelGateway>();
            var request = new ModelRequest(run.Model, run.History, sources, chosen, run.Parameters.Effort, run.Parameters.Web, null);
            var sink = sp.GetRequiredService<IApprovalSink>();
            await foreach (var e in gateway.StreamAsync(request, token))
            {
                var forward = e with { MessageId = messageId };
                switch (e.Event)
                {
                    case StreamEvents.ReasoningDelta:
                        reasoning.Append(e.Text);
                        break;
                    case StreamEvents.ReasoningEnd:
                        reasoningMs = e.Ms;
                        break;
                    case StreamEvents.Plan:
                        plan = e.Steps is null ? null : [.. e.Steps];
                        break;
                    case StreamEvents.ToolStart:
                        tools.Add(new MessageBlock("tool_call", ToolId: e.ToolId, Title: e.Name, Icon: e.Icon, State: "running", Arguments: e.Arguments));
                        break;
                    case StreamEvents.ToolResult:
                        var at = tools.FindIndex(t => t.ToolId == e.ToolId);
                        if (at >= 0)
                        {
                            tools[at] = tools[at] with { State = e.State ?? "done", Result = e.Result, Summary = e.Summary, Ms = e.Ms };
                        }
                        break;
                    case StreamEvents.ToolApproval:
                        // A tool that writes or reaches outside is never run from the chat without a person's approval: the approval feature (agents)
                        // keeps the ticket; without it the sink rejects at once.
                        var ticket = await sink.RequestAsync(new ApprovalRequest("chat", messageId, e.Name ?? "Tool", "Requested from a chat", [new("arguments", e.Arguments ?? "")], e.Title ?? "external", null), token);
                        var state = ticket is null ? "rejected" : "pending";
                        tools.Add(new MessageBlock("tool_call", ToolId: e.ToolId, Title: e.Name, Icon: e.Icon, State: state, Arguments: e.Arguments,
                            Summary: ticket is null ? "Approval is needed; nothing was run." : "Waiting for approval."));
                        forward = e with { MessageId = messageId, State = state };
                        break;
                    case StreamEvents.Citation:
                        citations.Add(new ChatCitation
                        {
                            Id = Guid.NewGuid(), MessageId = messageId, Position = e.Position ?? citations.Count + 1, DocumentId = e.DocumentId ?? Guid.Empty, ChunkId = e.ChunkId ?? Guid.Empty,
                            Score = e.Score ?? 0, Quote = Cut(e.Quote ?? "", 1000), DocumentName = Cut(e.Title ?? "", 260), Where = e.Name is null ? null : Cut(e.Name, 200),
                        });
                        break;
                    case StreamEvents.TextDelta:
                        text.Append(e.Text);
                        break;
                    case StreamEvents.Usage:
                        usage = e.Usage;
                        continue;
                    case StreamEvents.MessageError:
                        status = MessageStatuses.Failed;
                        error = Cut(e.Error ?? "The model failed.", 500);
                        continue;
                }
                generation.Add(forward);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            status = MessageStatuses.Stopped;
        }
        catch (ApiException e)
        {
            status = MessageStatuses.Failed;
            error = Cut(e.Message, 500);
        }
        catch (Exception e)
        {
            LogFailed(sp.GetRequiredService<ILogger<ChatGenerator>>(), messageId, e.GetType().Name);
            status = MessageStatuses.Failed;
            error = "The model could not answer.";
        }

        var total = (int)clock.ElapsedMilliseconds;
        var tokensIn = usage?.TokensIn ?? 0;
        var tokensOut = usage?.TokensOut ?? 0;
        if (usage is null && text.Length > 0)
        {
            var g = sp.GetRequiredService<IModelGateway>();
            tokensIn = run.History.Sum(m => g.CountTokens(m.Content));
            tokensOut = g.CountTokens(text.ToString());
        }
        var cache = usage?.TokensCache ?? 0;
        var cost = Pricing.Cost(run.Model.PriceIn, run.Model.PriceOut, run.Model.PriceCache, tokensIn, tokensOut, cache);
        var final = usage is null && tokensIn + tokensOut == 0 ? null : new MessageUsageDto(tokensIn, tokensOut, cache, usage?.FirstTokenMs ?? 0, usage?.TotalMs is > 0 ? usage.TotalMs : total, cost, usage?.Estimated ?? true);
        try
        {
            await FinishAsync(sp, run, status, error, reasoning.ToString(), reasoningMs, plan, tools, text.ToString(), citations, final);
        }
        catch (Exception e)
        {
            LogNotSaved(sp.GetRequiredService<ILogger<ChatGenerator>>(), messageId, e.GetType().Name);
            status = MessageStatuses.Failed;
            error = "The answer could not be saved.";
        }
        if (final is not null)
        {
            generation.Add(new ChatStreamEvent(StreamEvents.Usage, MessageId: messageId, Usage: final));
        }
        generation.Add(status == MessageStatuses.Failed
            ? new ChatStreamEvent(StreamEvents.MessageError, MessageId: messageId, Status: status, Error: error)
            : new ChatStreamEvent(StreamEvents.MessageDone, MessageId: messageId, Status: status));
        generation.Complete();
        _ = Task.Delay(KeepFinished).ContinueWith(_ => registry.Remove(messageId), TaskScheduler.Default);
    }

    private static async Task FinishAsync(IServiceProvider sp, GenerationRun run, string status, string? error, string reasoning, int? reasoningMs, List<PlanStepDto>? plan, List<MessageBlock> tools,
        string text, List<ChatCitation> citations, MessageUsageDto? usage)
    {
        var db = sp.GetRequiredService<ChatDbContext>();
        var time = sp.GetRequiredService<TimeProvider>();
        var message = await db.Messages.SingleAsync(m => m.Id == run.Generation.MessageId);
        List<MessageBlock> blocks = [];
        if (reasoning.Length > 0)
        {
            blocks.Add(new MessageBlock("reasoning", reasoning, Ms: reasoningMs));
        }
        if (plan is { Count: > 0 })
        {
            blocks.Add(new MessageBlock("plan", Steps: plan));
        }
        blocks.AddRange(tools);
        if (text.Length > 0)
        {
            blocks.Add(new MessageBlock("text", text));
        }
        var json = ChatMapping.ToJson(blocks);
        if (json.Length > ChatDbContext.MaxContent)
        {
            // An answer longer than the column keeps its beginning.
            var keep = Math.Max(0, text.Length - (json.Length - ChatDbContext.MaxContent) - 1000);
            blocks[^1] = new MessageBlock("text", text[..keep]);
            json = ChatMapping.ToJson(blocks);
        }
        message.ContentJson = json;
        message.SearchText = Cut(ChatMapping.PlainText(blocks), ChatDbContext.MaxContent);
        message.Status = status;
        message.Error = error;
        message.UsageJson = ChatMapping.ToJson(usage);
        db.Citations.AddRange(citations);
        var conversation = await db.Conversations.SingleAsync(c => c.Id == message.ConversationId);
        conversation.TokenTotal += (usage?.TokensIn ?? 0) + (usage?.TokensOut ?? 0) + (usage?.TokensCache ?? 0);
        conversation.CostTotal += usage?.Cost ?? 0;
        conversation.UpdatedAt = time.GetUtcNow();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateConcurrencyException e) when (attempt < 3)
            {
                // The conversation was renamed or moved meanwhile: keep that change, add ours on top.
                foreach (var entry in e.Entries)
                {
                    await entry.ReloadAsync();
                }
                message.ContentJson = json;
                message.Status = status;
                message.Error = error;
                message.UsageJson = ChatMapping.ToJson(usage);
                conversation.TokenTotal += (usage?.TokensIn ?? 0) + (usage?.TokensOut ?? 0) + (usage?.TokensCache ?? 0);
                conversation.CostTotal += usage?.Cost ?? 0;
                conversation.UpdatedAt = time.GetUtcNow();
            }
        }
        if (status == MessageStatuses.Done && usage is not null)
        {
            await sp.GetRequiredService<IUsageRecorder>().RecordAsync(new UsageEntry(run.ActorId, run.Model.Id, run.Model.ProviderId, run.Model.DisplayName, "chat", conversation.Id, usage.TokensIn, usage.TokensOut,
                usage.TokensCache, usage.FirstTokenMs, usage.TotalMs, usage.Cost, $"{{\"in\":{run.Model.PriceIn},\"out\":{run.Model.PriceOut},\"cache\":{run.Model.PriceCache}}}"), CancellationToken.None);
        }
        if (status == MessageStatuses.Done)
        {
            // The event carries ids and the model, not what was written.
            await sp.GetRequiredService<IEventPublisher>().PublishAsync(ChatEvents.MessageCompleted, ChatMapping.ToDto(message, 0, 1, []) with { Blocks = [] }, ContractsJsonContext.Default.MessageDto, CancellationToken.None);
        }
    }

    public async Task StopAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken) ?? throw ApiException.NotFound("The message");
        await chat.FindAsync(message.ConversationId, ownerOnly: true, cancellationToken);
        if (registry.Find(messageId) is { Completed: false } generation)
        {
            await generation.Cancel.CancelAsync();
        }
        else if (!MessageStatuses.IsFinal(message.Status))
        {
            // Nobody is writing it (the API restarted): close it.
            message.Status = MessageStatuses.Stopped;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>The events of an answer from <paramref name="lastEventId"/> on; for an answer that is not in memory any more, only how it ended.</summary>
    public async Task<(Generation? Generation, ChatMessage Message)> ResumeAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await db.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken) ?? throw ApiException.NotFound("The message");
        await chat.FindAsync(message.ConversationId, ownerOnly: false, cancellationToken);
        return (registry.Find(messageId), message);
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];

    [LoggerMessage(Level = LogLevel.Error, Message = "The answer {Message} failed: {Type}")]
    private static partial void LogFailed(ILogger logger, Guid message, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "The answer {Message} could not be saved: {Type}")]
    private static partial void LogNotSaved(ILogger logger, Guid message, string type);
}
