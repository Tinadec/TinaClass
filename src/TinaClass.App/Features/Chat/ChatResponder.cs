using System.Runtime.CompilerServices;
using TinaClass.App.Common.Api;
using TinaClass.App.Features.Models;
using TinaClass.Contracts.Chat;
using TKCode.Arc;

namespace TinaClass.App.Features.Chat;

/// <summary>
/// Arc's <c>AiChat</c> asks this for every answer. It makes sure the conversation exists on the server, decides whether the request
/// is a new message, a retry (same text: regenerate) or an edit, and turns the service's events into the block's: reasoning, tool
/// calls, sources and text pieces. The plan, the usage and the sources also go to <see cref="ChatSession.Inspector"/> for the right wing.
/// </summary>
public sealed class ChatResponder(IChatService chat, ChatHub hub, ChatSession session, IModelsService models) : IAiChatResponder
{
    public async IAsyncEnumerable<AiChatStreamEvent> RespondAsync(AiChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var conversation = request.Conversation;
        var modelCode = request.Model?.Id;
        var text = request.Messages.LastOrDefault(m => m.Role == AiChatRole.User)?.Content ?? string.Empty;
        var inspector = session.Inspector;
        inspector.Reset(request.Model?.DisplayName ?? "—");

        Guid id;
        if (!Guid.TryParse(conversation.Id, out id) || !session.Known.Contains(id))
        {
            string? failure = null;
            id = Guid.Empty;
            try
            {
                var created = await chat.CreateAsync(null, hub.NewChatFolder, null, modelCode, cancellationToken);
                id = created.Id;
            }
            catch (ServiceException e)
            {
                failure = e.Message;
            }
            if (failure is not null)
            {
                inspector.Status = GenerationStatus.Error;
                yield return new AiChatErrorEvent(failure);
                yield break;
            }
            session.Known.Add(id);
            conversation.Id = id.ToString();
            hub.NewChatFolder = null;
            hub.CurrentId = id;
        }

        var turns = session.TurnsOf(id);
        var turnIndex = request.Messages.Count(m => m.Role == AiChatRole.User) - 1;
        Guid? userId = null;
        Guid? answerId = null;
        IAsyncEnumerable<ChatStreamEvent> events;
        if (turnIndex < turns.Count)
        {
            var turn = turns[turnIndex];
            events = turn.Text == text
                ? chat.RegenerateAsync(turn.AssistantId, modelCode, cancellationToken)
                : chat.SendAsync(id, new SendMessageRequest(text, turn.ParentId, modelCode, null, false, null, null), cancellationToken);
            userId = turn.Text == text ? turn.UserId : null;
            turns.RemoveRange(turnIndex, turns.Count - turnIndex);
        }
        else
        {
            events = chat.SendAsync(id, new SendMessageRequest(text, turns.LastOrDefault()?.AssistantId, modelCode, null, false, null, null), cancellationToken);
        }

        var contextWindow = (await models.ModelsAsync(false, cancellationToken)).FirstOrDefault(m => m.Code == modelCode)?.ContextTokens ?? 128_000;
        var toolNames = new Dictionary<string, string>();
        var sources = new List<AiSource>();
        var sourcesSent = false;
        var finished = false;
        string? error = null;
        await using var enumerator = events.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            ChatStreamEvent e;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }
                e = enumerator.Current;
            }
            catch (ServiceException failure)
            {
                error = failure.Message;
                break;
            }
            catch (OperationCanceledException)
            {
                if (answerId is { } stopped)
                {
                    // Stop: what was generated stays; the server is told so it stops spending.
                    await StopQuietlyAsync(stopped);
                }
                throw;
            }

            switch (e.Event)
            {
                case StreamEvents.MessageStart:
                    answerId = e.MessageId;
                    userId = e.UserMessageId ?? userId;
                    break;
                case StreamEvents.ReasoningDelta when e.Text is { } thought:
                    yield return new AiChatReasoningDelta(thought);
                    break;
                case StreamEvents.ReasoningEnd:
                    yield return new AiChatReasoningEnd(e.Ms is { } ms ? TimeSpan.FromMilliseconds(ms) : null);
                    break;
                case StreamEvents.Plan when e.Steps is { } steps:
                    inspector.SetPlan(steps);
                    break;
                case StreamEvents.ToolStart when e.ToolId is { } toolId:
                    toolNames[toolId] = e.Name ?? e.Title ?? toolId;
                    yield return new AiChatToolUpdate(toolId, AiChatToolKind.Custom, AiChatToolStatus.Running, toolNames[toolId], Input: e.Arguments);
                    break;
                case StreamEvents.ToolResult when e.ToolId is { } toolId:
                    yield return new AiChatToolUpdate(toolId, AiChatToolKind.Custom, e.State == "error" ? AiChatToolStatus.Error : AiChatToolStatus.Done, toolNames.GetValueOrDefault(toolId, toolId),
                        Summary: e.Summary, Output: e.Result);
                    break;
                case StreamEvents.Citation when e.Position is { } position:
                    sources.Add(ChatMapper.ToSource(position, e.Title ?? string.Empty, e.Summary, e.Quote ?? string.Empty));
                    inspector.AddSource(position, e.Title ?? string.Empty, e.Summary, e.Quote ?? string.Empty, e.Score);
                    break;
                case StreamEvents.TextDelta when e.Text is { } piece:
                    if (!sourcesSent && sources.Count > 0)
                    {
                        sourcesSent = true;
                        yield return new AiChatSourcesEvent(sources);
                    }
                    inspector.Status = GenerationStatus.Streaming;
                    yield return new AiChatTextDelta(piece);
                    break;
                case StreamEvents.Usage when e.Usage is { } usage:
                    inspector.SetUsage(usage, contextWindow);
                    break;
                case StreamEvents.MessageDone:
                    finished = true;
                    inspector.Status = e.Status == MessageStatuses.Stopped ? GenerationStatus.Stopped : GenerationStatus.Done;
                    break;
                case StreamEvents.MessageError:
                    error = e.Error ?? ErrorText.For(e.Status ?? string.Empty);
                    break;
            }
        }

        if (!sourcesSent && sources.Count > 0)
        {
            yield return new AiChatSourcesEvent(sources);
        }
        if (error is not null)
        {
            inspector.Status = GenerationStatus.Error;
            yield return new AiChatErrorEvent(error);
        }
        if (finished && userId is { } user && answerId is { } answer)
        {
            turns.Add(new TurnIds(user, answer, text, turns.LastOrDefault()?.AssistantId));
        }
        await hub.RefreshAsync();
        if (hub.Find(id) is { } updated)
        {
            conversation.Title = updated.Title;
        }
    }

    private async Task StopQuietlyAsync(Guid messageId)
    {
        try
        {
            await chat.StopAsync(messageId);
        }
        catch (ServiceException)
        {
            // Already finished or the connection is gone: nothing to stop.
        }
    }
}
