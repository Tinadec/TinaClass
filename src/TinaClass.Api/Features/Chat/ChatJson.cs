using System.Text.Json;
using System.Text.Json.Serialization;
using TinaClass.Api.Features.Chat.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Chat;

namespace TinaClass.Api.Features.Chat;

/// <summary>What the person asked for with the last message (knowledge bases, tools, web, effort), kept on the conversation so that a regenerated answer is made the same way.</summary>
internal sealed record GenerationParams(IReadOnlyList<Guid>? KnowledgeBaseIds, IReadOnlyList<string>? ToolIds, bool Web, string? Effort);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GenerationParams))]
internal sealed partial class ChatJsonContext : JsonSerializerContext;

/// <summary>Messages as stored (JSON text columns) and as sent (DTOs).</summary>
internal static class ChatMapping
{
    public static string ToJson(IReadOnlyList<MessageBlock> blocks) => JsonSerializer.Serialize(blocks, ContractsJsonContext.Default.IReadOnlyListMessageBlock);

    public static IReadOnlyList<MessageBlock> Blocks(string json) =>
        JsonSerializer.Deserialize(json, ContractsJsonContext.Default.IReadOnlyListMessageBlock) ?? [];

    public static string? ToJson(MessageUsageDto? usage) => usage is null ? null : JsonSerializer.Serialize(usage, ContractsJsonContext.Default.MessageUsageDto);

    public static MessageUsageDto? Usage(string? json) => json is null ? null : JsonSerializer.Deserialize(json, ContractsJsonContext.Default.MessageUsageDto);

    public static GenerationParams? Params(string? json) => json is null ? null : JsonSerializer.Deserialize(json, ChatJsonContext.Default.GenerationParams);

    public static string ToJson(GenerationParams value) => JsonSerializer.Serialize(value, ChatJsonContext.Default.GenerationParams);

    /// <summary>The plain text of the blocks (what search looks through).</summary>
    public static string PlainText(IEnumerable<MessageBlock> blocks) =>
        string.Join('\n', blocks.Where(b => b.Kind == "text" && !string.IsNullOrEmpty(b.Text)).Select(b => b.Text));

    public static ConversationDto ToDto(Conversation c) =>
        new(c.Id, c.OwnerId, c.Title, c.FolderId, c.SpaceId, c.Pinned, c.Archived, c.ModelCode, c.ActiveLeafId, c.TokenTotal, c.CostTotal, c.UpdatedAt, c.Version);

    public static CitationDto ToDto(ChatCitation c) =>
        new(c.Id, c.MessageId, c.Position, c.DocumentId, c.ChunkId, c.Score, c.Quote, c.DocumentName, c.Where);

    public static MessageDto ToDto(ChatMessage m, int siblingIndex, int siblingCount, IReadOnlyList<CitationDto> citations) =>
        new(m.Id, m.ConversationId, m.ParentId, m.ParentId, m.Role, Blocks(m.ContentJson), m.Status, m.Error, m.ModelCode, Usage(m.UsageJson), m.Feedback, m.CreatedAt, siblingIndex, siblingCount, citations);

    /// <summary>A message as a shared link shows it: the tool calls keep their title and summary, but not their arguments and results (which can hold private data).</summary>
    public static MessageDto ForSharing(MessageDto m) => m with
    {
        Blocks = [.. m.Blocks.Select(b => b.Kind == "tool_call" ? b with { Arguments = null, Result = null } : b)],
        Feedback = null,
    };
}
