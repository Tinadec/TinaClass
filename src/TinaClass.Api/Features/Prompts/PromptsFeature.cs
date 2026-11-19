using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.Api.Common;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Prompts.Data;
using TinaClass.Api.Features.Prompts.Data.Sqlite;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Prompts;
using TinaClass.Contracts.Sample;

namespace TinaClass.Api.Features.Prompts;

/// <summary>/prompts: the prompt library with slash commands, versions and test cases (design 4.6). Provides <see cref="IPromptResolver"/> to chat.</summary>
public static class PromptsFeature
{
    public static IServiceCollection AddPromptsFeature(this IServiceCollection services)
    {
        services.AddFeatureDb<PromptsDbContext>(PromptsDbContext.Name)
                .WithSqlite<SqlitePromptsDbContext>()
                ;
        services.AddScoped<PromptsService>();
        services.Replace(ServiceDescriptor.Scoped<IPromptResolver, PromptResolver>());
        services.AddSingleton<ISampleSeeder, PromptsSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapPromptsFeature(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ApiRoutes.Prompts).WithTags("Prompts");
        group.MapGet("", (string? q, PromptsService s, CancellationToken ct) => s.ListAsync(q, ct)).RequirePermission(Permissions.PromptsRead);
        group.MapGet("/{id:guid}", (Guid id, PromptsService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(Permissions.PromptsRead);
        group.MapPost("", async (SavePromptRequest r, PromptsService s, CancellationToken ct) =>
        {
            var prompt = await s.CreateAsync(r, ct);
            return Results.Created($"{ApiRoutes.Prompts}/{prompt.Id}", prompt);
        }).RequirePermission(Permissions.PromptsWrite);
        group.MapPatch("/{id:guid}", (Guid id, SavePromptRequest r, PromptsService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(Permissions.PromptsWrite);
        group.MapDelete("/{id:guid}", async (Guid id, PromptsService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PromptsWrite);
        group.MapGet("/{id:guid}/versions", (Guid id, PromptsService s, CancellationToken ct) => s.VersionsAsync(id, ct)).RequirePermission(Permissions.PromptsRead);
        group.MapPost("/{id:guid}/publish", (Guid id, PublishPromptRequest r, PromptsService s, CancellationToken ct) => s.PublishAsync(id, r, ct)).RequirePermission(Permissions.PromptsWrite);
        group.MapPost("/{id:guid}/run", (Guid id, RunPromptRequest r, PromptsService s, CancellationToken ct) => s.RunAsync(id, r, ct)).RequirePermission(Permissions.PromptsWrite);
        group.MapGet("/{id:guid}/cases", (Guid id, PromptsService s, CancellationToken ct) => s.CasesAsync(id, ct)).RequirePermission(Permissions.PromptsRead);
        group.MapPost("/{id:guid}/cases", async (Guid id, SavePromptCaseRequest r, PromptsService s, CancellationToken ct) =>
        {
            var item = await s.AddCaseAsync(id, r, ct);
            return Results.Created($"{ApiRoutes.Prompts}/{id}/cases/{item.Id}", item);
        }).RequirePermission(Permissions.PromptsWrite);
        group.MapPut("/{id:guid}/cases/{caseId:guid}", (Guid id, Guid caseId, SavePromptCaseRequest r, PromptsService s, CancellationToken ct) => s.UpdateCaseAsync(id, caseId, r, ct)).RequirePermission(Permissions.PromptsWrite);
        group.MapDelete("/{id:guid}/cases/{caseId:guid}", async (Guid id, Guid caseId, PromptsService s, CancellationToken ct) =>
        {
            await s.DeleteCaseAsync(id, caseId, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.PromptsWrite);
        group.MapPost("/{id:guid}/cases/run", (Guid id, PromptsService s, CancellationToken ct) => s.RunCasesAsync(id, ct)).RequirePermission(Permissions.PromptsWrite);
        return endpoints;
    }
}

/// <summary>The seven sample prompts (design 16); <c>/review</c> has its three published versions and three passing test cases.</summary>
internal sealed class PromptsSeeder : ISampleSeeder
{
    public int Order => 35;

    public async Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<PromptsDbContext>();
        if (await db.Prompts.AnyAsync(cancellationToken))
        {
            return;
        }
        foreach (var (p, index) in PromptsSample.Prompts.Select((p, i) => (p, i)))
        {
            db.Prompts.Add(new Prompt
            {
                Id = p.Id, OwnerId = p.OwnerId, Name = p.Name, Command = p.Command, Visibility = "workspace", SystemText = p.System, UserText = p.User, CurrentVersion = p.Version, UseCount = p.Uses,
                UpdatedAt = sample.At(index + 1, 10, 0),
            });
            if (p.Id == SampleIds.Prompt(1))
            {
                foreach (var v in PromptsSample.ReviewVersions)
                {
                    db.Versions.Add(new PromptVersion { Id = Guid.NewGuid(), PromptId = p.Id, Number = v.Number, Note = v.Note, SystemText = p.System, UserText = p.User, CreatedBy = v.By, CreatedAt = sample.At(v.DaysAgo, 15, 30) });
                }
            }
            else
            {
                db.Versions.Add(new PromptVersion { Id = Guid.NewGuid(), PromptId = p.Id, Number = p.Version, Note = "", SystemText = p.System, UserText = p.User, CreatedBy = p.OwnerId, CreatedAt = sample.At(index + 1, 10, 0) });
            }
        }
        foreach (var (c, index) in PromptsSample.ReviewCases.Select((c, i) => (c, i)))
        {
            db.Cases.Add(new PromptCase { Id = Guid.NewGuid(), PromptId = SampleIds.Prompt(1), Name = c.Name, Input = c.Input, Expect = c.Expect, LastPassed = true, Sort = index });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
