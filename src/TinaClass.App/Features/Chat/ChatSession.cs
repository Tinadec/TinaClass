using TinaClass.Contracts.Chat;

namespace TinaClass.App.Features.Chat;

/// <summary>The ids of one turn on the server: what the user wrote and the answer that is shown.</summary>
public sealed record TurnIds(Guid UserId, Guid AssistantId, string Text, Guid? ParentId);

/// <summary>
/// What the chat page and the responder share: the inspector, and which server messages the turns of each conversation are. Arc's
/// <c>AiChat</c> keeps the turns itself and calls the responder with the whole history; the responder uses the ids here to tell a new
/// message from a retry (same text: regenerate the answer) or an edit (new text: a new message beside the old one).
/// </summary>
public sealed class ChatSession
{
    private readonly Dictionary<Guid, List<TurnIds>> _turns = [];

    public ChatInspectorViewModel Inspector { get; } = new();

    /// <summary>Conversations that exist on the server (opened or created in this run).</summary>
    public HashSet<Guid> Known { get; } = [];

    public List<TurnIds> TurnsOf(Guid conversationId)
    {
        if (!_turns.TryGetValue(conversationId, out var list))
        {
            list = [];
            _turns[conversationId] = list;
        }
        return list;
    }

    public void Forget(Guid conversationId) => _turns.Remove(conversationId);

    /// <summary>Remembers the turns of a conversation just opened: user message, shown answer.</summary>
    public void Load(Guid conversationId, IReadOnlyList<MessageDto> messages)
    {
        Known.Add(conversationId);
        var list = TurnsOf(conversationId);
        list.Clear();
        foreach (var user in messages.Where(m => m.Role == MessageRoles.User).OrderBy(m => m.CreatedAt))
        {
            var answer = messages.Where(m => m.Role == MessageRoles.Assistant && m.ParentId == user.Id).OrderBy(m => m.SiblingIndex).LastOrDefault();
            if (answer is not null)
            {
                list.Add(new TurnIds(user.Id, answer.Id, ChatMapper.TextOf(user), user.ParentId));
            }
        }
    }
}
