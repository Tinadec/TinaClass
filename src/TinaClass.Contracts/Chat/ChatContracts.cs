namespace TinaClass.Contracts.Chat;

/// <summary>The life of an assistant message (design 4.1). <c>Stopped</c> keeps what was generated; <c>Failed</c> says why.</summary>
public static class MessageStatuses
{
    public const string Queued = "queued";
    public const string Thinking = "thinking";
    public const string CallingTools = "calling_tools";
    public const string Streaming = "streaming";
    public const string Done = "done";
    public const string Stopped = "stopped";
    public const string Failed = "failed";

    public static bool IsFinal(string status) => status is Done or Stopped or Failed;
}

public static class MessageRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public sealed record FolderDto(Guid Id, Guid? ParentId, string Name, int Sort, int ConversationCount);

public sealed record CreateFolderRequest(string Name, Guid? ParentId);

public sealed record UpdateFolderRequest(string? Name, Guid? ParentId, bool MoveToRoot);

public sealed record SpaceDto(Guid Id, string Name, string Instruction, int ConversationCount);

public sealed record CreateSpaceRequest(string Name, string Instruction);

public sealed record ConversationDto(Guid Id, Guid OwnerId, string Title, Guid? FolderId, Guid? SpaceId, bool Pinned, bool Archived, string? ModelCode,
    Guid? ActiveLeafId, long TokenTotal, long CostTotal, DateTimeOffset UpdatedAt, int Version);

public sealed record CreateConversationRequest(string? Title, Guid? FolderId, Guid? SpaceId, string? ModelCode);

/// <summary>PATCH /conversations/{id}. A null field is left alone; <see cref="MoveToRoot"/> takes it out of its folder. A title set here is never replaced by the automatic one.</summary>
public sealed record UpdateConversationRequest(string? Title, Guid? FolderId, bool MoveToRoot, bool? Pinned, bool? Archived, string? ModelCode, Guid? ActiveLeafId, int Version);

/// <summary>One step of a plan; <see cref="State"/> is pending, running, done or error.</summary>
public sealed record PlanStepDto(string Id, string Title, string State, int? Ms);

/// <summary>
/// A piece of a message's content: <c>text</c>, <c>reasoning</c> (<see cref="Ms"/> is the thinking time), <c>plan</c> (<see cref="Steps"/>)
/// or <c>tool_call</c> (<see cref="ToolId"/> to <see cref="Result"/>).
/// </summary>
public sealed record MessageBlock(string Kind, string? Text = null, IReadOnlyList<PlanStepDto>? Steps = null, string? ToolId = null, string? Title = null,
    string? Icon = null, string? State = null, string? Arguments = null, string? Result = null, string? Summary = null, int? Ms = null);

public sealed record MessageUsageDto(int TokensIn, int TokensOut, int TokensCache, int FirstTokenMs, int TotalMs, long Cost, bool Estimated);

public sealed record CitationDto(Guid Id, Guid MessageId, int Position, Guid DocumentId, Guid ChunkId, double Score, string Quote, string DocumentName, string? Where);

/// <summary>A message of the tree. <see cref="SiblingIndex"/> and <see cref="SiblingCount"/> drive the "2 / 2" switch under regenerated answers.</summary>
public sealed record MessageDto(Guid Id, Guid ConversationId, Guid? ParentId, Guid? SiblingGroup, string Role, IReadOnlyList<MessageBlock> Blocks, string Status,
    string? Error, string? ModelCode, MessageUsageDto? Usage, string? Feedback, DateTimeOffset CreatedAt, int SiblingIndex, int SiblingCount, IReadOnlyList<CitationDto> Citations);

/// <summary>POST /conversations/{id}/messages (answered as a stream of <see cref="ChatStreamEvent"/>).</summary>
public sealed record SendMessageRequest(string Text, Guid? ParentId, string? ModelCode, IReadOnlyList<Guid>? KnowledgeBaseIds, bool Web, IReadOnlyList<string>? ToolIds, string? Effort);

public sealed record RegenerateRequest(string? ModelCode);

public sealed record FeedbackRequest(string Value);

/// <summary>The SSE event names (design 7). Their order for one answer: message.start, reasoning.delta*, plan, tool.start, tool.result, citation*, text.delta*, usage, message.done.</summary>
public static class StreamEvents
{
    public const string MessageStart = "message.start";
    public const string ReasoningDelta = "reasoning.delta";
    public const string ReasoningEnd = "reasoning.end";
    public const string Plan = "plan";
    public const string ToolStart = "tool.start";
    public const string ToolResult = "tool.result";
    public const string ToolApproval = "tool.approval";
    public const string Citation = "citation";
    public const string TextDelta = "text.delta";
    public const string Usage = "usage";
    public const string MessageDone = "message.done";
    public const string MessageError = "message.error";
}

/// <summary>
/// One streamed event, flat on purpose (one shape for the wire, the app and the providers). Which fields are filled depends on
/// <see cref="Event"/>; see <see cref="StreamEvents"/>. The SSE <c>id:</c> of an event is its sequence number (for <c>Last-Event-ID</c>).
/// </summary>
public sealed record ChatStreamEvent(string Event, Guid? MessageId = null, Guid? UserMessageId = null, string? Model = null, string? Text = null,
    IReadOnlyList<PlanStepDto>? Steps = null, string? ToolId = null, string? Name = null, string? Icon = null, string? Arguments = null, string? Result = null,
    string? Summary = null, string? State = null, int? Position = null, Guid? DocumentId = null, Guid? ChunkId = null, double? Score = null, string? Title = null,
    string? Quote = null, MessageUsageDto? Usage = null, string? Status = null, string? Error = null, int? Ms = null);

public sealed record SearchResultDto(string Kind, Guid Id, string Title, string Snippet);

public sealed record CreateShareRequest(int? ExpiresInDays);

/// <summary>The link is shown once: only a hash of <see cref="Token"/> is stored.</summary>
public sealed record ShareDto(Guid Id, string Token, DateTimeOffset? ExpiresAt);

/// <summary>GET /shared/{token}: the snapshot taken when the link was made, readable without signing in.</summary>
public sealed record SharedConversationDto(string Title, DateTimeOffset SharedAt, IReadOnlyList<MessageDto> Messages);

public sealed record CitationDetailDto(Guid Id, string DocumentName, string? Where, string Quote, string Before, string After, double Score);

public static class ChatEvents
{
    public const string ConversationCreated = "conversation.created";
    public const string MessageCompleted = "message.completed";
}

/// <summary>What the audit log records for the chat feature.</summary>
public static class ChatAuditActions
{
    public const string Shared = "chat.shared";
    public const string ShareRevoked = "chat.share_revoked";
    public const string ConversationDeleted = "chat.conversation_deleted";
}
