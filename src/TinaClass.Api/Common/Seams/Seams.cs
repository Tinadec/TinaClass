using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Sample;

namespace TinaClass.Api.Common.Seams;

// Cross-feature seams (design 5). A feature that needs something from another one asks for one of these interfaces; the feature that
// provides it replaces the default registered by AddCommon (with the default, the asking feature still builds and works, with less).

/// <summary>A model as the chat and the agents need it: where it is served from and what it costs (minor units per million tokens).</summary>
public sealed record ResolvedModel(Guid Id, string Code, string DisplayName, Guid ProviderId, string Protocol, string BaseUrl, int ContextTokens, long PriceIn, long PriceOut, long PriceCache,
    IReadOnlyList<string> Efforts);

/// <summary>Provided by Models. The default knows only the built-in Echo model, so that the chat works without the Models feature's tables.</summary>
public interface IModelCatalog
{
    /// <summary>The model with <paramref name="code"/>, or the default one when it is null; null when there is none enabled.</summary>
    Task<ResolvedModel?> ResolveAsync(string? code, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResolvedModel>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The key of a provider, for the adapter that calls it. Never returned by any endpoint; null when none is set.</summary>
    Task<string?> GetApiKeyAsync(Guid providerId, CancellationToken cancellationToken);
}

/// <summary>The default catalog: only the built-in Echo model (<c>echo-1</c>).</summary>
public sealed class DefaultModelCatalog : IModelCatalog
{
    public static readonly ResolvedModel Echo = new(SampleIds.Model(6), "echo-1", "Echo", SampleIds.Provider(5), "echo", "builtin://echo",
        128_000, 0, 0, 0, ["fast"]);

    public Task<ResolvedModel?> ResolveAsync(string? code, CancellationToken cancellationToken) => Task.FromResult<ResolvedModel?>(Echo);

    public Task<IReadOnlyList<ResolvedModel>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ResolvedModel>>([Echo]);

    public Task<string?> GetApiKeyAsync(Guid providerId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}

/// <summary>One message of a conversation sent to a model.</summary>
public sealed record ModelMessage(string Role, string Content);

/// <summary>What a provider adapter is asked. <see cref="Sources"/> are the retrieved passages (empty without a knowledge base); <see cref="Tools"/> the tools the person switched on.</summary>
public sealed record ModelRequest(ResolvedModel Model, IReadOnlyList<ModelMessage> Messages, IReadOnlyList<RetrievedChunk> Sources, IReadOnlyList<ToolInfo> Tools, string? Effort, bool Web, string? ApiKey);

/// <summary>
/// An adapter to one kind of model service. <see cref="StreamAsync"/> answers with the design's event stream (reasoning, plan, tool calls,
/// citations, text, usage); the API frames them as SSE. The default <c>EchoProvider</c> needs no key and no network.
/// </summary>
public interface IModelProvider
{
    /// <summary>The <c>mdl_provider.protocol</c> this adapter serves.</summary>
    string Protocol { get; }

    IAsyncEnumerable<ChatStreamEvent> StreamAsync(ModelRequest request, CancellationToken cancellationToken);

    /// <summary>Rough token count for budgets when the service reports none (about four characters per token).</summary>
    int CountTokens(string text);
}

/// <summary>
/// Provided by Models: the adapter for a model's provider (by protocol), with the provider's key filled in. Chat, prompts, agents and workflows
/// all talk to models through it.
/// </summary>
public interface IModelGateway
{
    IAsyncEnumerable<ChatStreamEvent> StreamAsync(ModelRequest request, CancellationToken cancellationToken);

    int CountTokens(string text);
}

public sealed record Completion(string Text, int TokensIn, int TokensOut, long Cost, string ModelCode);

public static class ModelGatewayExtensions
{
    /// <summary>Asks a model one question and collects the whole answer (prompts, agents and workflow nodes do not stream to a person).</summary>
    public static async Task<Completion> CompleteAsync(this IModelGateway gateway, IModelCatalog catalog, string? modelCode, IReadOnlyList<ModelMessage> messages,
        CancellationToken cancellationToken)
    {
        var model = await catalog.ResolveAsync(modelCode, cancellationToken) ?? throw new Common.Http.ApiException(StatusCodes.Status409Conflict, Contracts.Common.ErrorCodes.NoModel, "No model is enabled.");
        var text = new System.Text.StringBuilder();
        int tokensIn = 0, tokensOut = 0, cache = 0;
        await foreach (var e in gateway.StreamAsync(new ModelRequest(model, messages, [], [], null, false, null), cancellationToken))
        {
            switch (e.Event)
            {
                case StreamEvents.TextDelta:
                    text.Append(e.Text);
                    break;
                case StreamEvents.Usage when e.Usage is { } u:
                    (tokensIn, tokensOut, cache) = (u.TokensIn, u.TokensOut, u.TokensCache);
                    break;
                case StreamEvents.MessageError:
                    throw new Common.Http.ApiException(StatusCodes.Status502BadGateway, Contracts.Common.ErrorCodes.ProviderFailed, e.Error ?? "The model failed.");
            }
        }
        if (tokensIn == 0 && tokensOut == 0)
        {
            tokensIn = gateway.CountTokens(string.Join('\n', messages.Select(m => m.Content)));
            tokensOut = gateway.CountTokens(text.ToString());
        }
        return new Completion(text.ToString(), tokensIn, tokensOut, Pricing.Cost(model.PriceIn, model.PriceOut, model.PriceCache, tokensIn, tokensOut, cache), model.Code);
    }
}

public sealed record RetrievedChunk(Guid DocumentId, Guid ChunkId, string DocumentName, string? Where, double Score, string Text, string BaseName);

public sealed record ChunkContext(string DocumentName, string? Where, string Text, string Before, string After);

/// <summary>Provided by Knowledge. The default returns nothing.</summary>
public interface IKnowledgeRetriever
{
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, IReadOnlyList<Guid> baseIds, int topK, CancellationToken cancellationToken);

    Task<ChunkContext?> GetChunkAsync(Guid chunkId, CancellationToken cancellationToken);
}

public sealed record ToolInfo(Guid Id, string Name, string Risk);

/// <summary>Provided by Agents. The default has no tools, so the composer shows no "tools" chip.</summary>
public interface IToolCatalog
{
    Task<IReadOnlyList<ToolInfo>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Provided by Prompts. The default leaves the text as written.</summary>
public interface IPromptResolver
{
    /// <summary>Expands a leading <c>/command arguments</c> into the prompt's text; returns the text unchanged when it is not a command.</summary>
    Task<string> ResolveAsync(string text, CancellationToken cancellationToken);
}

public sealed record UsageEntry(Guid? UserId, Guid? ModelId, Guid? ProviderId, string ModelName, string SubjectKind, Guid SubjectId, int TokensIn, int TokensOut, int TokensCache, int FirstTokenMs, int TotalMs, long Cost,
    string PriceSnapshot);

/// <summary>Provided by Usage. The default records nothing.</summary>
public interface IUsageRecorder
{
    Task RecordAsync(UsageEntry entry, CancellationToken cancellationToken);
}

/// <summary>Provided by Usage. The default lets every request through.</summary>
public interface IBudgetGuard
{
    /// <summary>Throws a 402-style <c>budget_exceeded</c> problem when the budget is over and its action is <c>block</c>.</summary>
    Task EnsureAsync(Guid? userId, CancellationToken cancellationToken);
}

public sealed record ApprovalRequest(string Source, Guid SourceId, string Title, string Detail, IReadOnlyList<KeyValuePair<string, string>> Arguments, string Risk, Guid? RunId);

/// <summary>Provided by Agents. The default rejects at once, so that nothing that needs a person's approval runs without one.</summary>
public interface IApprovalSink
{
    /// <summary>Asks for approval; returns the ticket, or null when it was rejected on the spot.</summary>
    Task<Guid?> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken);
}

public sealed record ApprovalDecision(Guid Id, string Source, Guid SourceId, bool Approved, string? Reason);

/// <summary>Told when a person decides an approval (the workflow feature resumes its run).</summary>
public interface IApprovalObserver
{
    Task OnDecidedAsync(ApprovalDecision decision, CancellationToken cancellationToken);
}

public sealed record DirectoryUser(Guid Id, string Email, string Name, string RoleId, bool Disabled, DateTimeOffset CreatedAt);

/// <summary>Provided by Auth: the accounts as the team feature lists and changes them (it never touches the auth tables).</summary>
public interface IUserDirectory
{
    Task<IReadOnlyList<DirectoryUser>> ListAsync(CancellationToken cancellationToken);

    Task<DirectoryUser?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<DirectoryUser?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task ChangeRoleAsync(Guid id, string roleId, CancellationToken cancellationToken);

    Task SetDisabledAsync(Guid id, bool disabled, CancellationToken cancellationToken);

    /// <summary>Creates an account for an accepted invitation.</summary>
    Task<DirectoryUser> CreateAsync(string email, string name, string password, string roleId, CancellationToken cancellationToken);
}

public sealed class NullKnowledgeRetriever : IKnowledgeRetriever
{
    public Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, IReadOnlyList<Guid> baseIds, int topK, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RetrievedChunk>>([]);

    public Task<ChunkContext?> GetChunkAsync(Guid chunkId, CancellationToken cancellationToken) => Task.FromResult<ChunkContext?>(null);
}

public sealed class NullToolCatalog : IToolCatalog
{
    public Task<IReadOnlyList<ToolInfo>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ToolInfo>>([]);
}

public sealed class NullPromptResolver : IPromptResolver
{
    public Task<string> ResolveAsync(string text, CancellationToken cancellationToken) => Task.FromResult(text);
}

public sealed class NullUsageRecorder : IUsageRecorder
{
    public Task RecordAsync(UsageEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class NullBudgetGuard : IBudgetGuard
{
    public Task EnsureAsync(Guid? userId, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class RejectingApprovalSink : IApprovalSink
{
    public Task<Guid?> RequestAsync(ApprovalRequest request, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
}
