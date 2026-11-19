using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Chat.Data;
using TinaClass.Api.Features.Chat.Data.Sqlite;
using TinaClass.Contracts;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Sample;

namespace TinaClass.Api.Features.Chat;

/// <summary>Conversations and answers: /conversations, /messages, /folders, /spaces, /search, /shares, /shared and /citations (the core feature).</summary>
public static class ChatFeature
{
    public static IServiceCollection AddChatFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFeatureDb<ChatDbContext>(ChatDbContext.Name)
                .WithSqlite<SqliteChatDbContext>()
                ;
        services.AddOptions<ChatOptions>().Bind(configuration.GetSection("Chat"));
        services.AddSingleton<GenerationRegistry>();
        services.AddScoped<ChatService>();
        services.AddScoped<ChatGenerator>();
        services.AddScoped<ChatPurger>();
        services.AddHostedService<ChatPurgeJob>();
        services.AddSingleton<IDatabaseInitializer, ChatInitializer>();
        services.AddSingleton<ISampleSeeder, ChatSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapChatFeature(this IEndpointRouteBuilder endpoints)
    {
        var conversations = endpoints.MapGroup(ApiRoutes.Conversations).WithTags("Chat");
        conversations.MapGet("", (string? q, Guid? folderId, Guid? spaceId, bool? archived, bool? all, string? sort, int? page, int? pageSize, ChatService chat, CancellationToken ct) =>
            chat.ListAsync(q, folderId, spaceId, archived, all == true, sort, page, pageSize, ct)).RequirePermission(Permissions.ChatUse);
        conversations.MapPost("", async (CreateConversationRequest r, ChatService chat, CancellationToken ct) =>
        {
            var c = await chat.CreateAsync(r, ct);
            return Results.Created($"{ApiRoutes.Conversations}/{c.Id}", c);
        }).RequirePermission(Permissions.ChatUse);
        conversations.MapGet("/{id:guid}", (Guid id, ChatService chat, CancellationToken ct) => chat.GetAsync(id, ct)).RequirePermission(Permissions.ChatUse);
        conversations.MapPatch("/{id:guid}", (Guid id, UpdateConversationRequest r, ChatService chat, CancellationToken ct) => chat.UpdateAsync(id, r, ct)).RequirePermission(Permissions.ChatUse);
        conversations.MapDelete("/{id:guid}", async (Guid id, ChatService chat, CancellationToken ct) =>
        {
            await chat.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ChatUse);
        conversations.MapGet("/{id:guid}/messages", (Guid id, Guid? leaf, ChatService chat, CancellationToken ct) => chat.MessagesAsync(id, leaf, ct)).RequirePermission(Permissions.ChatUse);
        // The answer is a stream of server-sent events (design 7); the request ends when the answer is complete.
        conversations.MapPost("/{id:guid}/messages", async (Guid id, SendMessageRequest r, HttpContext http, ChatGenerator generator, CancellationToken ct) =>
            await StreamAsync(http, await generator.SendAsync(id, r, ct), 0, ct)).RequirePermission(Permissions.ChatUse);
        conversations.MapPost("/{id:guid}/share", (Guid id, CreateShareRequest r, ChatService chat, CancellationToken ct) => chat.ShareAsync(id, r, ct)).RequirePermission(Permissions.ChatShare);

        var messages = endpoints.MapGroup(ApiRoutes.Messages).WithTags("Chat");
        messages.MapPost("/{id:guid}/regenerate", async (Guid id, RegenerateRequest r, HttpContext http, ChatGenerator generator, CancellationToken ct) =>
            await StreamAsync(http, await generator.RegenerateAsync(id, r, ct), 0, ct)).RequirePermission(Permissions.ChatUse);
        messages.MapPost("/{id:guid}/stop", async (Guid id, ChatGenerator generator, CancellationToken ct) =>
        {
            await generator.StopAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ChatUse);
        messages.MapPost("/{id:guid}/feedback", async (Guid id, FeedbackRequest r, ChatService chat, CancellationToken ct) =>
        {
            await chat.FeedbackAsync(id, r, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ChatUse);
        // Reconnect: the events of an answer from the one after Last-Event-ID.
        messages.MapGet("/{id:guid}/stream", async (Guid id, HttpContext http, ChatGenerator generator, CancellationToken ct) =>
        {
            var from = int.TryParse(http.Request.Headers["Last-Event-ID"].ToString(), out var last) && last >= 0 ? last + 1 : 0;
            var (generation, message) = await generator.ResumeAsync(id, ct);
            if (generation is not null)
            {
                await StreamAsync(http, generation, from, ct);
                return;
            }
            // Not in memory any more (it finished a while ago, or the API restarted): say how it ended.
            var ended = MessageStatuses.IsFinal(message.Status) ? message.Status : MessageStatuses.Failed;
            var final = ended == MessageStatuses.Failed
                ? new ChatStreamEvent(StreamEvents.MessageError, MessageId: id, Status: ended, Error: message.Error ?? "The answer is not available.")
                : new ChatStreamEvent(StreamEvents.MessageDone, MessageId: id, Status: ended);
            await WriteHeadersAsync(http);
            await WriteEventAsync(http, 0, final, ct);
        }).RequirePermission(Permissions.ChatUse);

        var folders = endpoints.MapGroup(ApiRoutes.Folders).WithTags("Chat");
        folders.MapGet("", (ChatService chat, CancellationToken ct) => chat.FoldersAsync(ct)).RequirePermission(Permissions.ChatUse);
        folders.MapPost("", async (CreateFolderRequest r, ChatService chat, CancellationToken ct) =>
        {
            var f = await chat.CreateFolderAsync(r, ct);
            return Results.Created($"{ApiRoutes.Folders}/{f.Id}", f);
        }).RequirePermission(Permissions.ChatUse);
        folders.MapPatch("/{id:guid}", (Guid id, UpdateFolderRequest r, ChatService chat, CancellationToken ct) => chat.UpdateFolderAsync(id, r, ct)).RequirePermission(Permissions.ChatUse);
        folders.MapDelete("/{id:guid}", async (Guid id, ChatService chat, CancellationToken ct) =>
        {
            await chat.DeleteFolderAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ChatUse);

        var spaces = endpoints.MapGroup(ApiRoutes.Spaces).WithTags("Chat");
        spaces.MapGet("", (ChatService chat, CancellationToken ct) => chat.SpacesAsync(ct)).RequirePermission(Permissions.ChatUse);
        spaces.MapPost("", async (CreateSpaceRequest r, ChatService chat, CancellationToken ct) =>
        {
            var s = await chat.CreateSpaceAsync(r, ct);
            return Results.Created($"{ApiRoutes.Spaces}/{s.Id}", s);
        }).RequirePermission(Permissions.ChatUse);

        endpoints.MapGet(ApiRoutes.Search, (string? q, string? scope, ChatService chat, CancellationToken ct) => chat.SearchAsync(q, scope, ct)).WithTags("Chat").RequirePermission(Permissions.ChatUse);
        endpoints.MapGet(ApiRoutes.Citations + "/{id:guid}", (Guid id, ChatService chat, CancellationToken ct) => chat.CitationAsync(id, ct)).WithTags("Chat").RequirePermission(Permissions.ChatUse);
        endpoints.MapDelete(ApiRoutes.Shares + "/{id:guid}", async (Guid id, ChatService chat, CancellationToken ct) =>
        {
            await chat.RevokeShareAsync(id, ct);
            return Results.NoContent();
        }).WithTags("Chat").RequirePermission(Permissions.ChatShare);
        // The one anonymous read of the product: whoever has the link sees the snapshot. Rate limited like the sign-in routes.
        endpoints.MapGet(ApiRoutes.Shared + "/{token}", (string token, ChatService chat, CancellationToken ct) => chat.SharedAsync(token, ct))
                 .WithTags("Chat").AllowAnonymous().RequireRateLimiting(CommonServices.AuthRateLimit);
        return endpoints;
    }

    private static Task WriteHeadersAsync(HttpContext http)
    {
        var response = http.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        return response.WriteAsync(": open\n\n", http.RequestAborted);
    }

    private static async Task WriteEventAsync(HttpContext http, int id, ChatStreamEvent e, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(e, ContractsJsonContext.Default.ChatStreamEvent);
        await http.Response.WriteAsync($"id: {id}\nevent: {e.Event}\ndata: {json}\n\n", cancellationToken);
        await http.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task StreamAsync(HttpContext http, Generation generation, int from, CancellationToken cancellationToken)
    {
        await WriteHeadersAsync(http);
        try
        {
            await foreach (var (id, e) in generation.ReadAsync(from, cancellationToken))
            {
                await WriteEventAsync(http, id, e, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The client went away; the answer keeps being written and can be picked up again with /stream.
        }
    }
}

/// <summary><c>Chat:PurgeAfterDays</c>: how long a deleted conversation is kept before it is removed for good.</summary>
public sealed class ChatOptions
{
    public int PurgeAfterDays { get; set; } = 30;
}

/// <summary>Removes conversations deleted more than <c>Chat:PurgeAfterDays</c> days ago, with their messages, citations and share links.</summary>
public sealed class ChatPurger(ChatDbContext db, Microsoft.Extensions.Options.IOptions<ChatOptions> options, TimeProvider time)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().AddDays(-Math.Max(options.Value.PurgeAfterDays, 0));
        var doomed = await db.Conversations.Where(c => c.DeletedAt != null && c.DeletedAt < cutoff).Select(c => c.Id).ToListAsync(cancellationToken);
        if (doomed.Count == 0)
        {
            return 0;
        }
        await db.Shares.Where(s => doomed.Contains(s.ConversationId)).ExecuteDeleteAsync(cancellationToken);
        await db.Conversations.Where(c => doomed.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
        return doomed.Count;
    }
}

/// <summary>Runs <see cref="ChatPurger"/> every <c>Jobs:ChatPurgeMinutes</c> minutes (0 turns it off; tests call the purger directly).</summary>
internal sealed partial class ChatPurgeJob(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time, ILogger<ChatPurgeJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = configuration.GetValue("Jobs:ChatPurgeMinutes", 360);
        if (minutes <= 0)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ChatPurger>().RunAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The purge of deleted conversations failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>An answer that was being written when the API stopped will never finish: close it as failed so that nobody waits for it.</summary>
internal sealed class ChatInitializer : IDatabaseInitializer
{
    public int Order => 30;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ChatDbContext>();
        await db.Messages.Where(m => m.Status == MessageStatuses.Queued || m.Status == MessageStatuses.Thinking || m.Status == MessageStatuses.CallingTools || m.Status == MessageStatuses.Streaming)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MessageStatuses.Failed).SetProperty(m => m.Error, "The API restarted while this answer was written."), cancellationToken);
    }
}

/// <summary>The sample conversations of 栖云科技: eleven conversations in three folders; the first has two turns, two answers to the second (2 / 2), sources and tool calls.</summary>
internal sealed class ChatSeeder : ISampleSeeder
{
    public int Order => 40;

    public async Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ChatDbContext>();
        if (await db.Conversations.AnyAsync(cancellationToken))
        {
            return;
        }
        foreach (var space in SampleCore.Spaces)
        {
            db.Spaces.Add(new ChatSpace { Id = space.Id, OwnerId = SampleIds.Owner, Name = space.Name, Instruction = space.Instruction });
        }
        foreach (var folder in SampleCore.Folders)
        {
            db.Folders.Add(new ChatFolder { Id = folder.Id, OwnerId = SampleIds.Owner, ParentId = folder.ParentId, Name = folder.Name, Sort = folder.Sort });
        }
        var model = SampleCore.DefaultModel;
        foreach (var c in SampleCore.Conversations)
        {
            var messages = SampleChat.Messages(c.Id, sample.Now);
            var last = messages[^1];
            db.Conversations.Add(new Conversation
            {
                Id = c.Id, OwnerId = c.OwnerId, FolderId = c.FolderId, Title = c.Title, TitleSetByUser = true, Pinned = c.Pinned, ModelCode = model.Code, ActiveLeafId = last.Id,
                TokenTotal = messages.Where(m => m.Usage is not null).Sum(m => (long)(m.Usage!.TokensIn + m.Usage.TokensOut + m.Usage.TokensCache)),
                CostTotal = messages.Where(m => m.Usage is not null).Sum(m => m.Usage!.Cost), CreatedAt = messages[0].CreatedAt, UpdatedAt = sample.At(c.DaysAgo, c.Hour, c.Minute),
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        foreach (var c in SampleCore.Conversations)
        {
            foreach (var m in SampleChat.Messages(c.Id, sample.Now))
            {
                db.Messages.Add(new ChatMessage
                {
                    Id = m.Id, ConversationId = c.Id, ParentId = m.ParentId, Role = m.Role, Status = m.Status, ModelCode = m.ModelCode, CreatedAt = m.CreatedAt,
                    ContentJson = ChatMapping.ToJson(m.Blocks), SearchText = ChatMapping.PlainText(m.Blocks), UsageJson = ChatMapping.ToJson(m.Usage),
                });
                foreach (var citation in m.Citations)
                {
                    db.Citations.Add(new ChatCitation { Id = citation.Id, MessageId = m.Id, Position = citation.Position, DocumentId = citation.DocumentId, ChunkId = citation.ChunkId,
                        Score = citation.Score, Quote = citation.Quote, DocumentName = citation.DocumentName, Where = citation.Where });
                }
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
