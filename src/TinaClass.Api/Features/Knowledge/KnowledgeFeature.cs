using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.Api.Common;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Knowledge.Data;
using TinaClass.Api.Features.Knowledge.Data.Sqlite;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Knowledge;
using TinaClass.Contracts.Sample;

namespace TinaClass.Api.Features.Knowledge;

/// <summary>/knowledge and /documents: bases, documents, the index and the retrieval test (design 4.2). Provides <see cref="IKnowledgeRetriever"/> to chat and agents.</summary>
public static class KnowledgeFeature
{
    public static IServiceCollection AddKnowledgeFeature(this IServiceCollection services)
    {
        services.AddFeatureDb<KnowledgeDbContext>(KnowledgeDbContext.Name)
                .WithSqlite<SqliteKnowledgeDbContext>()
                ;
        services.AddSingleton<IVectorIndex, InProcessVectorIndex>();
        services.AddScoped<KnowledgeService>();
        services.AddScoped<IndexingService>();
        services.Replace(ServiceDescriptor.Scoped<IKnowledgeRetriever, KnowledgeRetriever>());
        services.AddHostedService<IndexerJob>();
        services.AddSingleton<ISampleSeeder, KnowledgeSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapKnowledgeFeature(this IEndpointRouteBuilder endpoints)
    {
        var bases = endpoints.MapGroup(ApiRoutes.Knowledge).WithTags("Knowledge");
        bases.MapGet("", (KnowledgeService s, CancellationToken ct) => s.ListAsync(ct)).RequirePermission(Permissions.KnowledgeRead);
        bases.MapPost("", async (CreateKnowledgeBaseRequest r, KnowledgeService s, CancellationToken ct) =>
        {
            var kb = await s.CreateAsync(r, ct);
            return Results.Created($"{ApiRoutes.Knowledge}/{kb.Id}", kb);
        }).RequirePermission(Permissions.KnowledgeManage);
        bases.MapPatch("/{id:guid}", (Guid id, UpdateKnowledgeBaseRequest r, KnowledgeService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(Permissions.KnowledgeManage);
        bases.MapDelete("/{id:guid}", async (Guid id, KnowledgeService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.KnowledgeManage);
        bases.MapPost("/{id:guid}/search", (Guid id, SearchKnowledgeRequest r, KnowledgeService s, CancellationToken ct) => s.SearchAsync(id, r, ct)).RequirePermission(Permissions.KnowledgeRead);
        bases.MapGet("/{id:guid}/documents", (Guid id, string? state, string? q, int? page, int? pageSize, KnowledgeService s, CancellationToken ct) => s.DocumentsAsync(id, state, q, page, pageSize, ct))
             .RequirePermission(Permissions.KnowledgeRead);
        bases.MapPost("/{id:guid}/documents", async (Guid id, CreateDocumentRequest r, KnowledgeService s, CancellationToken ct) =>
        {
            var document = await s.AddDocumentAsync(id, r, ct);
            return Results.Created($"{ApiRoutes.Documents}/{document.Id}", document);
        }).RequirePermission(Permissions.KnowledgeWrite);

        var documents = endpoints.MapGroup(ApiRoutes.Documents).WithTags("Knowledge");
        documents.MapDelete("/{id:guid}", async (Guid id, KnowledgeService s, CancellationToken ct) =>
        {
            await s.DeleteDocumentAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.KnowledgeWrite);
        documents.MapPost("/{id:guid}/reindex", (Guid id, KnowledgeService s, CancellationToken ct) => s.ReindexAsync(id, ct)).RequirePermission(Permissions.KnowledgeWrite);
        return endpoints;
    }
}

/// <summary>
/// The sample bases (design 16): the four bases with their document and chunk counts (the first base's nine named documents tell every
/// state, the rest are numbered files that only carry their chunk count) and the ten chunks retrieval finds.
/// </summary>
internal sealed class KnowledgeSeeder : ISampleSeeder
{
    public int Order => 30;

    public async Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<KnowledgeDbContext>();
        if (await db.Bases.AnyAsync(cancellationToken))
        {
            return;
        }
        foreach (var b in KnowledgeSample.Bases)
        {
            db.Bases.Add(new KbBase { Id = b.Id, OwnerId = SampleIds.Owner, Name = b.Name, Description = b.Description, Visibility = b.Visibility, CreatedAt = sample.At(30, 9, 0), EmbeddingModelCode = "nebula-embed-v2" });
        }
        var named = KnowledgeSample.Documents.ToDictionary(d => d.Id);
        foreach (var d in KnowledgeSample.Documents)
        {
            db.Documents.Add(new KbDocument
            {
                Id = d.Id, BaseId = d.BaseId, Name = d.Name, Mime = d.Mime, Size = d.Size, Hash = Secrets.Hash(d.Name), State = d.State, Progress = d.Progress, ChunkCount = d.Chunks, Error = d.Error,
                UploaderId = d.UploaderId, CreatedAt = sample.Now.AddMinutes(-d.MinutesAgo),
                // The documents still in progress carry a short text, so that the indexer can finish them in the live demo.
                Content = d.State is DocumentStates.Embedding or DocumentStates.Parsing or DocumentStates.Queued ? $"# {d.Name}\n\nSample text of {d.Name}." : null,
            });
        }
        foreach (var b in KnowledgeSample.Bases)
        {
            var namedHere = KnowledgeSample.Documents.Where(d => d.BaseId == b.Id).ToList();
            var others = b.Documents - namedHere.Count;
            var chunks = KnowledgeSample.Spread(b.Chunks - namedHere.Sum(d => d.Chunks), others).ToList();
            var uploaders = new[] { SampleIds.Admin, SampleIds.He, SampleIds.Zhou, SampleIds.Tang, SampleIds.Owner };
            for (var i = 0; i < others; i++)
            {
                var name = $"{b.Name} {i + 1:000}.md";
                db.Documents.Add(new KbDocument
                {
                    Id = Guid.NewGuid(), BaseId = b.Id, Name = name, Mime = "text/markdown", Size = 20_000 + i * 977 % 400_000, Hash = Secrets.Hash(name), State = DocumentStates.Ready, Progress = 100,
                    ChunkCount = chunks[i], UploaderId = uploaders[i % uploaders.Length], CreatedAt = sample.Now.AddMinutes(-(20_000 + i * 53)),
                });
            }
        }
        foreach (var c in KnowledgeSample.Chunks)
        {
            db.Chunks.Add(new KbChunk
            {
                Id = c.Id, DocumentId = c.DocumentId, BaseId = named[c.DocumentId].BaseId, Ordinal = c.Ordinal, Text = c.Text, Page = c.Page, Heading = c.Heading, TokenCount = KnowledgeText.Tokens(c.Text).Count,
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
