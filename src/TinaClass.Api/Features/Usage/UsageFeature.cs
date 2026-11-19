using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TinaClass.Api.Common;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Usage.Data;
using TinaClass.Api.Features.Usage.Data.Sqlite;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Sample;
using TinaClass.Contracts.Usage;

namespace TinaClass.Api.Features.Usage;

/// <summary>/usage: what the models cost, the detail list and the budget (design 4.5). Provides <see cref="IUsageRecorder"/> and <see cref="IBudgetGuard"/> to chat, agents and prompts.</summary>
public static class UsageFeature
{
    public static IServiceCollection AddUsageFeature(this IServiceCollection services)
    {
        services.AddFeatureDb<UsageDbContext>(UsageDbContext.Name)
                .WithSqlite<SqliteUsageDbContext>()
                ;
        services.AddScoped<UsageService>();
        services.Replace(ServiceDescriptor.Scoped<IUsageRecorder, UsageRecorder>());
        services.Replace(ServiceDescriptor.Scoped<IBudgetGuard, BudgetGuard>());
        services.AddSingleton<ISampleSeeder, UsageSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapUsageFeature(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ApiRoutes.Usage).WithTags("Usage");
        group.MapGet("/summary", (DateOnly? from, DateOnly? to, UsageService s, CancellationToken ct) => s.SummaryAsync(from, to, ct)).RequirePermission(Permissions.UsageRead);
        group.MapGet("/records", (DateOnly? from, DateOnly? to, int? page, int? pageSize, UsageService s, CancellationToken ct) => s.RecordsAsync(from, to, page, pageSize, ct))
             .RequirePermission(Permissions.UsageRead);
        group.MapGet("/budget", (UsageService s, CancellationToken ct) => s.GetBudgetAsync(ct)).RequirePermission(Permissions.UsageRead);
        group.MapPut("/budget", (UpdateBudgetRequest r, UsageService s, CancellationToken ct) => s.PutBudgetAsync(r, ct)).RequirePermission(Permissions.UsageManage);
        return endpoints;
    }
}

/// <summary>The sample month (design 16): the daily rows of <see cref="UsageSample"/> up to today, the 2,000 yuan budget at 50 and 80 percent, and a handful of detail rows.</summary>
internal sealed class UsageSeeder : ISampleSeeder
{
    public int Order => 60;

    public async Task SeedAsync(IServiceProvider services, SampleContext sample, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<UsageDbContext>();
        if (await db.Budgets.AnyAsync(cancellationToken))
        {
            return;
        }
        var today = DateOnly.FromDateTime(sample.Now.UtcDateTime);
        var names = SampleCore.Models.ToDictionary(m => m.Id, m => m.Name);
        var last = UsageSample.DailyCost.Count - 1;
        foreach (var r in UsageSample.Rows())
        {
            db.Daily.Add(new DailyUsage
            {
                Id = Guid.NewGuid(), Day = today.AddDays(r.DayIndex - last), ModelId = r.ModelId, ModelName = names.GetValueOrDefault(r.ModelId, "Echo"), UserId = r.UserId ?? Guid.Empty,
                Requests = r.Requests, Tokens = r.Tokens, CacheTokens = r.CacheTokens, FirstTokenMsTotal = r.FirstTokenMsTotal, Cost = r.Cost,
            });
        }
        var spent = UsageSample.DailyCost.Sum();
        db.Budgets.Add(new Budget { Id = Guid.NewGuid(), Scope = "workspace", ScopeId = Guid.Empty, Amount = UsageSample.Budget, Action = BudgetActions.Notify,
            AlertMonth = $"{today.Year:0000}-{today.Month:00}", AlertState = BudgetStates.For(spent, UsageSample.Budget) });
        db.Budgets.Add(new Budget { Id = Guid.NewGuid(), Scope = "user", ScopeId = Guid.Empty, Amount = 30_000, Action = BudgetActions.Notify });
        // The newest requests, for the detail list: what the day's heaviest model did, spread over the day.
        var people = new[] { SampleIds.Owner, SampleIds.Admin, SampleIds.He, SampleIds.Zhou, SampleIds.Tang };
        for (var i = 0; i < 24; i++)
        {
            var model = SampleCore.Models[i % 3];
            var tokensIn = 800 + i * 137 % 2_400;
            var tokensOut = 300 + i * 211 % 1_800;
            db.Records.Add(new UsageRecord
            {
                Id = Guid.NewGuid(), UserId = people[i % people.Length], ModelId = model.Id, ProviderId = model.ProviderId, ModelName = model.Name, SubjectKind = i % 5 == 4 ? "agent" : "chat", SubjectId = Guid.NewGuid(),
                TokensIn = tokensIn, TokensOut = tokensOut, TokensCache = tokensIn * 31 / 100, FirstTokenMs = 320 + i * 17 % 400, TotalMs = 1_800 + i * 263 % 4_000,
                Cost = Pricing.Cost(model.PriceIn, model.PriceOut, model.PriceCache, tokensIn, tokensOut, tokensIn * 31 / 100),
                PriceSnapshot = $"{model.PriceIn}/{model.PriceOut}/{model.PriceCache}", At = sample.At(0, 9, 0).AddMinutes(-i * 17),
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
