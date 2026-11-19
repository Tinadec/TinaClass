using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Usage.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Usage;

namespace TinaClass.Api.Features.Usage;

/// <summary>Writes what a request cost (<see cref="IUsageRecorder"/>) and keeps the month's budget state; announces a threshold once a month.</summary>
public sealed class UsageRecorder(UsageDbContext db, IEventPublisher events, TimeProvider time) : IUsageRecorder
{
    public async Task RecordAsync(UsageEntry entry, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        db.Records.Add(new UsageRecord
        {
            Id = Guid.NewGuid(), UserId = entry.UserId, ModelId = entry.ModelId, ProviderId = entry.ProviderId, ModelName = Cut(entry.ModelName, 80), SubjectKind = entry.SubjectKind, SubjectId = entry.SubjectId,
            TokensIn = entry.TokensIn, TokensOut = entry.TokensOut, TokensCache = entry.TokensCache, FirstTokenMs = entry.FirstTokenMs, TotalMs = entry.TotalMs, Cost = entry.Cost,
            PriceSnapshot = Cut(entry.PriceSnapshot, 300), At = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await AddToDailyAsync(entry, DateOnly.FromDateTime(now.UtcDateTime), cancellationToken);
        await CheckThresholdAsync(DateOnly.FromDateTime(now.UtcDateTime), cancellationToken);
    }

    private async Task AddToDailyAsync(UsageEntry entry, DateOnly day, CancellationToken cancellationToken)
    {
        var modelId = entry.ModelId ?? Guid.Empty;
        var user = entry.UserId ?? Guid.Empty;
        var tokens = (long)entry.TokensIn + entry.TokensOut + entry.TokensCache;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            // A counter: the last write may win, so a single UPDATE that adds is right (and cannot lose a concurrent request).
            var updated = await db.Daily.Where(d => d.Day == day && d.ModelId == modelId && d.UserId == user).ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Requests, d => d.Requests + 1).SetProperty(d => d.Tokens, d => d.Tokens + tokens).SetProperty(d => d.CacheTokens, d => d.CacheTokens + entry.TokensCache)
                .SetProperty(d => d.FirstTokenMsTotal, d => d.FirstTokenMsTotal + entry.FirstTokenMs).SetProperty(d => d.Cost, d => d.Cost + entry.Cost), cancellationToken);
            if (updated > 0)
            {
                return;
            }
            try
            {
                db.Daily.Add(new DailyUsage { Id = Guid.NewGuid(), Day = day, ModelId = modelId, ModelName = Cut(entry.ModelName, 80), UserId = user, Requests = 1, Tokens = tokens, CacheTokens = entry.TokensCache,
                    FirstTokenMsTotal = entry.FirstTokenMs, Cost = entry.Cost });
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException)
            {
                // Another request created the row first: add to it instead.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task CheckThresholdAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var budget = await db.Budgets.SingleOrDefaultAsync(b => b.Scope == "workspace", cancellationToken);
        if (budget is null || budget.Amount <= 0)
        {
            return;
        }
        var (start, end) = UsageMonths.Of(today);
        var spent = await db.Daily.Where(d => d.Day >= start && d.Day <= end).SumAsync(d => (long?)d.Cost, cancellationToken) ?? 0;
        var state = BudgetStates.For(spent, budget.Amount);
        var month = UsageMonths.Key(today);
        var known = budget.AlertMonth == month ? budget.AlertState ?? BudgetStates.Ok : BudgetStates.Ok;
        if (state == known)
        {
            return;
        }
        budget.AlertMonth = month;
        budget.AlertState = state;
        await db.SaveChangesAsync(cancellationToken);
        if (Rank(state) > Rank(known))
        {
            await events.PublishAsync(UsageEvents.ThresholdCrossed, UsageService.ToDto(budget, spent, 0), ContractsJsonContext.Default.BudgetDto, cancellationToken);
        }
    }

    private static int Rank(string state) => state switch { BudgetStates.Warn50 => 1, BudgetStates.Warn80 => 2, BudgetStates.Over => 3, _ => 0 };

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}

internal static class UsageMonths
{
    public static (DateOnly Start, DateOnly End) Of(DateOnly day) => (new DateOnly(day.Year, day.Month, 1), new DateOnly(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month)));

    public static string Key(DateOnly day) => $"{day.Year:0000}-{day.Month:00}";
}

/// <summary>Refuses a request when the budget is spent and its action is <c>block</c> (402 <c>budget_exceeded</c>); with <c>notify</c> nothing is refused.</summary>
public sealed class BudgetGuard(UsageDbContext db, TimeProvider time) : IBudgetGuard
{
    public async Task EnsureAsync(Guid? userId, CancellationToken cancellationToken)
    {
        var budgets = await db.Budgets.AsNoTracking().Where(b => b.Action == BudgetActions.Block && b.Amount > 0).ToListAsync(cancellationToken);
        if (budgets.Count == 0)
        {
            return;
        }
        var (start, end) = UsageMonths.Of(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
        if (budgets.FirstOrDefault(b => b.Scope == "workspace") is { } workspace
            && (await db.Daily.Where(d => d.Day >= start && d.Day <= end).SumAsync(d => (long?)d.Cost, cancellationToken) ?? 0) >= workspace.Amount)
        {
            throw new ApiException(StatusCodes.Status402PaymentRequired, ErrorCodes.BudgetExceeded, "The workspace's budget for this month is spent.");
        }
        if (userId is { } id && budgets.FirstOrDefault(b => b.Scope == "user") is { } personal
            && (await db.Daily.Where(d => d.Day >= start && d.Day <= end && d.UserId == id).SumAsync(d => (long?)d.Cost, cancellationToken) ?? 0) >= personal.Amount)
        {
            throw new ApiException(StatusCodes.Status402PaymentRequired, ErrorCodes.BudgetExceeded, "Your budget for this month is spent.");
        }
    }
}

/// <summary>The cost screen (design 4.5): summary, the detail list and the budget. Someone with only <c>usage.read</c> sees their own use.</summary>
public sealed class UsageService(UsageDbContext db, IActorContext actors, IUserDirectory users, IAuditLog audit, TimeProvider time)
{
    private const string BudgetUpdatedAction = "usage.budget_updated";

    private async Task<(Actor Actor, bool All)> ScopeAsync(CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        return (actor, actor.Can(Permissions.UsageManage));
    }

    private DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    public async Task<UsageSummaryDto> SummaryAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var (actor, all) = await ScopeAsync(cancellationToken);
        var today = Today;
        var end = to ?? today;
        var start = from ?? UsageMonths.Of(end).Start;
        if (start > end || end.DayNumber - start.DayNumber > 366)
        {
            throw ApiException.Invalid("from", "Use a range of at most a year, oldest day first.");
        }
        var rows = await db.Daily.AsNoTracking().Where(d => d.Day >= start && d.Day <= end && (all || d.UserId == actor.Id)).ToListAsync(cancellationToken);
        var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(i => start.AddDays(i))
                             .Select(day => rows.Where(r => r.Day == day).ToList())
                             .Select((list, i) => new DailyUsageDto(start.AddDays(i), list.Sum(r => r.Cost), list.Sum(r => r.Requests), list.Sum(r => r.Tokens))).ToList();
        var models = rows.GroupBy(r => r.ModelName).Select(g => new ModelUsageDto(g.Key, g.Sum(r => r.Cost), g.Sum(r => r.Tokens), g.Sum(r => r.Requests)))
                         .OrderByDescending(m => m.Cost).ThenBy(m => m.ModelName, StringComparer.Ordinal).ToList();
        var directory = (await users.ListAsync(cancellationToken)).ToDictionary(u => u.Id);
        var members = rows.GroupBy(r => r.UserId).Select(g =>
        {
            var known = directory.GetValueOrDefault(g.Key);
            return new MemberUsageDto(g.Key == Guid.Empty ? null : g.Key, known?.Name ?? "Agents", known?.RoleId ?? "", g.Sum(r => r.Cost), g.Sum(r => r.Tokens), g.Sum(r => r.Requests));
        }).OrderByDescending(m => m.Cost).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();
        var requests = rows.Sum(r => r.Requests);
        var tokens = rows.Sum(r => r.Tokens);
        var budget = await db.Budgets.AsNoTracking().SingleOrDefaultAsync(b => b.Scope == "workspace", cancellationToken);
        var (monthStart, monthEnd) = UsageMonths.Of(end);
        var monthSpent = await db.Daily.AsNoTracking().Where(d => d.Day >= monthStart && d.Day <= monthEnd).SumAsync(d => (long?)d.Cost, cancellationToken) ?? 0;
        var amount = budget?.Amount ?? 0;
        var projected = end == today ? UsageSample.Projected(today, monthSpent, amount, today.Day) : null;
        return new UsageSummaryDto(start, end, rows.Sum(r => r.Cost), amount, tokens, requests, requests == 0 ? 0 : (int)(rows.Sum(r => r.FirstTokenMsTotal) / requests),
            tokens == 0 ? 0 : (int)(rows.Sum(r => r.CacheTokens) * 100 / tokens), BudgetStates.For(monthSpent, amount), projected, days, models, members);
    }

    public async Task<Paged<UsageRecordDto>> RecordsAsync(DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var (actor, all) = await ScopeAsync(cancellationToken);
        var paging = PageRequest.Of(page, pageSize);
        var query = db.Records.AsNoTracking().Where(r => all || r.UserId == actor.Id);
        if (from is { } f)
        {
            var start = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(r => r.At >= start);
        }
        if (to is { } t)
        {
            var end = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(r => r.At < end);
        }
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(r => r.At).ThenBy(r => r.Id).Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellationToken);
        var names = (await users.ListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.Name);
        return paging.Wrap<UsageRecordDto>([.. rows.Select(r => new UsageRecordDto(r.Id, r.UserId, r.UserId is { } u ? names.GetValueOrDefault(u, "") : "Agents", r.ModelName, r.SubjectKind,
            r.TokensIn, r.TokensOut, r.TokensCache, r.FirstTokenMs, r.TotalMs, r.Cost, r.At))], total);
    }

    public async Task<BudgetDto> GetBudgetAsync(CancellationToken cancellationToken)
    {
        var workspace = await db.Budgets.AsNoTracking().SingleOrDefaultAsync(b => b.Scope == "workspace", cancellationToken);
        var personal = await db.Budgets.AsNoTracking().SingleOrDefaultAsync(b => b.Scope == "user", cancellationToken);
        var (start, end) = UsageMonths.Of(Today);
        var spent = await db.Daily.AsNoTracking().Where(d => d.Day >= start && d.Day <= end).SumAsync(d => (long?)d.Cost, cancellationToken) ?? 0;
        return workspace is null ? new BudgetDto(0, [50, 80], BudgetActions.Notify, spent, BudgetStates.Ok, personal?.Amount ?? 0, 0) : ToDto(workspace, spent, personal?.Amount ?? 0);
    }

    public async Task<BudgetDto> PutBudgetAsync(UpdateBudgetRequest request, CancellationToken cancellationToken)
    {
        var validator = new Validator();
        if (request.Amount is < 0 or > 1_000_000_000_000)
        {
            validator.Add("amount", "Use zero or a positive amount in minor units.");
        }
        if (request.PersonalAmount is < 0 or > 1_000_000_000_000)
        {
            validator.Add("personalAmount", "Use zero or a positive amount in minor units.");
        }
        if (request.Action is not (BudgetActions.Notify or BudgetActions.Block))
        {
            validator.Add("action", "Use notify or block.");
        }
        validator.ThrowIfInvalid();
        var workspace = await db.Budgets.SingleOrDefaultAsync(b => b.Scope == "workspace", cancellationToken);
        if (workspace is null)
        {
            workspace = new Budget { Id = Guid.NewGuid(), Scope = "workspace", ScopeId = Guid.Empty };
            db.Budgets.Add(workspace);
        }
        else
        {
            db.ExpectVersion(workspace, request.Version);
        }
        workspace.Amount = request.Amount;
        workspace.Action = request.Action;
        var personal = await db.Budgets.SingleOrDefaultAsync(b => b.Scope == "user", cancellationToken);
        if (personal is null)
        {
            personal = new Budget { Id = Guid.NewGuid(), Scope = "user", ScopeId = Guid.Empty };
            db.Budgets.Add(personal);
        }
        personal.Amount = request.PersonalAmount;
        personal.Action = request.Action;
        // A new amount starts the month's alerts again.
        workspace.AlertMonth = null;
        workspace.AlertState = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(BudgetUpdatedAction, "budget", workspace.Id.ToString(), "workspace"), cancellationToken);
        return await GetBudgetAsync(cancellationToken);
    }

    internal static BudgetDto ToDto(Budget b, long spent, long personal) =>
        new(b.Amount, [.. b.WarnAt.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)], b.Action, spent, BudgetStates.For(spent, b.Amount), personal, b.Version);
}
