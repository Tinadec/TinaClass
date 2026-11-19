using TinaClass.App.Common.Sample;
using TinaClass.Contracts.Sample;
using TinaClass.Contracts.Usage;

namespace TinaClass.App.Features.Usage;

/// <summary>
/// The sample month of 栖云科技: 14 days ending today, ¥1,284.60 of the ¥2,000 budget. The figures are summed from the sample's daily rows, so
/// any range adds up exactly (a 7-day range is the last seven of the same rows).
/// </summary>
public sealed class SampleUsageService : IUsageService
{
    public const int DaysInMonth = 14;

    private static readonly IReadOnlyList<SampleDailyRow> s_rows = UsageSample.Rows();

    public static DateOnly Today => DateOnly.FromDateTime(SampleClock.Now.LocalDateTime);

    public Task<UsageSummaryDto> SummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellation = default)
    {
        // The sample's "month" is its 14 days: a range that starts on the 1st takes all of them (the first sample day may be the day before).
        var first = from.Day == 1 ? 0 : Math.Clamp(from.DayNumber - Today.DayNumber + DaysInMonth - 1, 0, DaysInMonth - 1);
        var last = Math.Clamp(to.DayNumber - Today.DayNumber + DaysInMonth - 1, first, DaysInMonth - 1);
        var rows = s_rows.Where(r => r.DayIndex >= first && r.DayIndex <= last).ToList();
        var days = Enumerable.Range(first, last - first + 1).Select(i =>
        {
            var day = rows.Where(r => r.DayIndex == i).ToList();
            return new DailyUsageDto(Today.AddDays(i - DaysInMonth + 1), day.Sum(r => r.Cost), day.Sum(r => r.Requests), day.Sum(r => r.Tokens));
        }).ToList();
        var models = rows.GroupBy(r => r.ModelId).Select(g => new ModelUsageDto(SampleCore.Models.First(m => m.Id == g.Key).Name, g.Sum(r => r.Cost), g.Sum(r => r.Tokens), g.Sum(r => r.Requests)))
                         .OrderByDescending(m => m.Cost).ToList();
        var members = rows.GroupBy(r => r.UserId).Select(g =>
        {
            var person = SampleCore.People.FirstOrDefault(p => p.Id == g.Key);
            return new MemberUsageDto(g.Key, SampleCore.NameOf(g.Key), person?.Role ?? "agent", g.Sum(r => r.Cost), g.Sum(r => r.Tokens), g.Sum(r => r.Requests));
        }).OrderByDescending(m => m.Cost).ToList();
        var total = rows.Sum(r => r.Cost);
        var requests = rows.Sum(r => r.Requests);
        var tokens = rows.Sum(r => r.Tokens);
        var spentThisMonth = s_rows.Sum(r => r.Cost);
        return Task.FromResult(new UsageSummaryDto(days[0].Day, days[^1].Day, total, UsageSample.Budget, tokens, requests,
            requests == 0 ? 0 : (int)(rows.Sum(r => r.FirstTokenMsTotal) / requests), tokens == 0 ? 0 : (int)(rows.Sum(r => r.CacheTokens) * 100 / tokens),
            BudgetStates.For(spentThisMonth, UsageSample.Budget), UsageSample.Projected(Today, spentThisMonth, UsageSample.Budget, DaysInMonth), days, models, members));
    }

    public Task<BudgetDto> BudgetAsync(CancellationToken cancellation = default)
    {
        var spent = s_rows.Sum(r => r.Cost);
        return Task.FromResult(new BudgetDto(UsageSample.Budget, [50, 80, 100], BudgetActions.Notify, spent, BudgetStates.For(spent, UsageSample.Budget), 0, 1));
    }
}
