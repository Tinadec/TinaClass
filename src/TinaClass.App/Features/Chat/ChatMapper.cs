using TKCode.Arc;
using TinaClass.Contracts.Chat;

namespace TinaClass.App.Features.Chat;

/// <summary>Turns the contract's messages into the objects of Arc's <see cref="AiChat"/> block (and a conversation's title into a row).</summary>
public static class ChatMapper
{
    /// <summary>
    /// The turns of a conversation from its messages: each user message with the answers below it. A regenerated answer is another
    /// version of the same turn; the shown one (the last of the branch) is selected.
    /// </summary>
    public static AiChatConversation ToConversation(ConversationDto conversation, IReadOnlyList<MessageDto> messages)
    {
        var chat = new AiChatConversation { Id = conversation.Id.ToString(), Title = conversation.Title, ModelId = conversation.ModelCode };
        var users = messages.Where(m => m.Role == MessageRoles.User).OrderBy(m => m.CreatedAt).ToList();
        foreach (var user in users)
        {
            var answers = messages.Where(m => m.Role == MessageRoles.Assistant && m.ParentId == user.Id).OrderBy(m => m.SiblingIndex).ThenBy(m => m.CreatedAt).ToList();
            if (answers.Count == 0)
            {
                continue;
            }
            var turn = new AiChatTurn(new AiChatUserMessage { Text = TextOf(user) }, ToReply(answers[0]));
            foreach (var other in answers.Skip(1))
            {
                turn.Versions.Add(new AiChatTurnVersion { Prompt = new AiChatUserMessage { Text = TextOf(user) }, Reply = ToReply(other) });
            }
            turn.SelectedVersionIndex = answers.Count - 1;
            chat.Turns.Add(turn);
        }
        return chat;
    }

    public static string TextOf(MessageDto message) => string.Concat(message.Blocks.Where(b => b.Kind == "text").Select(b => b.Text));

    public static AiChatReply ToReply(MessageDto message)
    {
        var reply = new AiChatReply
        {
            Text = TextOf(message),
            ModelId = message.ModelCode,
            Status = message.Status switch
            {
                MessageStatuses.Failed => AiChatReplyStatus.Failed,
                MessageStatuses.Stopped => AiChatReplyStatus.Stopped,
                _ => AiChatReplyStatus.Done,
            },
            ErrorMessage = message.Error,
            Feedback = message.Feedback switch { "up" => AiChatFeedback.Up, "down" => AiChatFeedback.Down, _ => AiChatFeedback.None },
        };
        if (message.Blocks.FirstOrDefault(b => b.Kind == "reasoning") is { } reasoning)
        {
            reply.Reasoning = reasoning.Text ?? "";
            reply.ThoughtFor = reasoning.Ms is { } ms ? TimeSpan.FromMilliseconds(ms) : null;
        }
        foreach (var block in message.Blocks.Where(b => b.Kind == "tool_call"))
        {
            reply.Tools.Add(ToTool(block.ToolId ?? Guid.NewGuid().ToString(), block.Title ?? string.Empty, block.Summary, block.Arguments, block.Result, block.State == "error" ? AiChatToolStatus.Error : AiChatToolStatus.Done));
        }
        foreach (var citation in message.Citations.OrderBy(c => c.Position))
        {
            reply.Sources.Add(ToSource(citation.Position, citation.DocumentName, citation.Where, citation.Quote));
        }
        return reply;
    }

    public static AiChatTool ToTool(string id, string label, string? summary, string? input, string? output, AiChatToolStatus status) => new()
    {
        Id = id,
        Kind = AiChatToolKind.Custom,
        Status = status,
        RunningLabel = label,
        DoneLabel = label,
        Summary = summary,
        Input = input,
        Output = output,
    };

    public static AiSource ToSource(int position, string title, string? where, string quote) => new() { Index = position, Title = title, Domain = where ?? string.Empty, Snippet = quote };
}
