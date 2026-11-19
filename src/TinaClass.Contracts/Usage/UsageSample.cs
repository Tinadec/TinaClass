using TinaClass.Contracts.Sample;

namespace TinaClass.Contracts.Usage;

/// <summary>One row of the daily aggregate: a day, a model, a user (null for the agents' own runs).</summary>
public sealed record SampleDailyRow(int DayIndex, Guid ModelId, Guid? UserId, int Requests, long Tokens, long CacheTokens, long FirstTokenMsTotal, long Cost);

/// <summary>
/// The sample month: 14 days from 11/1 (a Sunday) to 11/14, ¥1,284.60 in total of a ¥2,000 budget, 48.2M tokens in 12,846 requests,
/// 0.48 s to the first token, 31% from the cache. The daily series, the split by model and the split by member each add up to the total.
/// </summary>
public static class UsageSample
{
    public const long Budget = 200_000;

    /// <summary>Cost per day in fen, the last element is today; the weekend (days 0, 6, 7 and 13 in November 2026) is low.</summary>
    public static IReadOnlyList<long> DailyCost { get; } = [1_860, 9_640, 10_420, 8_890, 12_150, 13_280, 2_430, 1_690, 11_870, 12_640, 10_980, 14_120, 14_860, 3_630];

    public static IReadOnlyList<(Guid ModelId, long Cost)> ByModel { get; } =
    [
        (SampleIds.Model(1), 74_230), (SampleIds.Model(3), 31_840), (SampleIds.Model(2), 18_620), (SampleIds.Model(5), 3_770), (SampleIds.Model(4), 0),
    ];

    /// <summary>Cost, tokens and requests of each member; the last one (no user id) is the agents running on their own.</summary>
    public static IReadOnlyList<(Guid? UserId, long Cost, int Requests)> ByMember { get; } =
    [
        (SampleIds.Owner, 31_240, 2_914), (SampleIds.Admin, 26_810, 2_406), (SampleIds.He, 24_170, 2_231), (SampleIds.Zhou, 19_830, 1_870), (SampleIds.Tang, 15_490, 1_492), (null, 10_920, 1_933),
    ];

    public const int TotalRequests = 12_846;
    public const long TotalTokens = 48_200_000;
    public const int AverageFirstTokenMs = 480;
    public const int CachePercent = 31;

    /// <summary>
    /// The rows of <c>use_daily</c>. Each (day, model) cell is split among the members that still have cost to give (largest deficit
    /// first), so every day, model and member total is exact; requests and tokens follow each member's cost.
    /// </summary>
    public static IReadOnlyList<SampleDailyRow> Rows()
    {
        var deficit = ByMember.Select(m => m.Cost).ToArray();
        var modelShare = ByModel.Select(m => m.Cost).ToArray();
        // Cell supplies: per day, split by model with exact column totals (greedy by remaining column capacity).
        var remainingModel = (long[])modelShare.Clone();
        var cells = new List<(int Day, int Model, long Cost)>();
        for (var d = 0; d < DailyCost.Count; d++)
        {
            var left = DailyCost[d];
            var totalLeft = remainingModel.Sum();
            var shares = new long[remainingModel.Length];
            long assigned = 0;
            for (var m = 0; m < remainingModel.Length; m++)
            {
                shares[m] = d == DailyCost.Count - 1 ? remainingModel[m] : left * remainingModel[m] / totalLeft;
                assigned += shares[m];
            }
            // Rounding remainder to the model with the most capacity left.
            if (d != DailyCost.Count - 1)
            {
                var rest = left - assigned;
                var order = Enumerable.Range(0, shares.Length).OrderByDescending(i => remainingModel[i] - shares[i]).ToArray();
                for (var k = 0; rest > 0; k = (k + 1) % order.Length)
                {
                    if (shares[order[k]] < remainingModel[order[k]])
                    {
                        shares[order[k]]++;
                        rest--;
                    }
                }
            }
            for (var m = 0; m < shares.Length; m++)
            {
                remainingModel[m] -= shares[m];
                if (shares[m] > 0)
                {
                    cells.Add((d, m, shares[m]));
                }
            }
        }
        var rows = new List<(int Day, int Model, int Member, long Cost)>();
        foreach (var (day, model, cost) in cells)
        {
            var left = cost;
            while (left > 0)
            {
                var member = Array.IndexOf(deficit, deficit.Max());
                var take = Math.Min(left, deficit[member]);
                rows.Add((day, model, member, take));
                deficit[member] -= take;
                left -= take;
            }
        }
        // Requests and tokens of a member follow the cost of their rows (largest remainder keeps the member's total exact).
        var result = new List<SampleDailyRow>();
        var tokensOfMember = ByMember.Select(m => TotalTokens * m.Requests / TotalRequests).ToArray();
        tokensOfMember[0] += TotalTokens - tokensOfMember.Sum();
        for (var member = 0; member < ByMember.Count; member++)
        {
            var mine = rows.Where(r => r.Member == member).ToList();
            var requests = Spread(ByMember[member].Requests, mine.Select(r => r.Cost).ToArray());
            var tokens = Spread(tokensOfMember[member], mine.Select(r => r.Cost).ToArray());
            for (var i = 0; i < mine.Count; i++)
            {
                var r = mine[i];
                result.Add(new SampleDailyRow(r.Day, ByModel[r.Model].ModelId, ByMember[member].UserId, (int)requests[i], tokens[i], tokens[i] * CachePercent / 100,
                    requests[i] * AverageFirstTokenMs, r.Cost));
            }
        }
        return [.. result.OrderBy(r => r.DayIndex).ThenBy(r => r.ModelId).ThenBy(r => r.UserId)];
    }

    /// <summary>Splits <paramref name="total"/> in proportion to <paramref name="weights"/> so that the parts add up to it exactly.</summary>
    public static long[] Spread(long total, long[] weights)
    {
        var sum = weights.Sum();
        var parts = weights.Select(w => total * w / sum).ToArray();
        var rest = total - parts.Sum();
        var order = Enumerable.Range(0, parts.Length).OrderByDescending(i => weights[i]).ToArray();
        for (var k = 0; rest > 0; k = (k + 1) % order.Length)
        {
            parts[order[k]]++;
            rest--;
        }
        return parts;
    }

    /// <summary>The day the budget runs out if the days so far repeat: today plus the remaining budget over the average day (rounded up).</summary>
    public static DateOnly? Projected(DateOnly today, long spent, long budget, int daysElapsed)
    {
        if (spent <= 0 || budget <= spent || daysElapsed <= 0)
        {
            return null;
        }
        var perDay = (double)spent / daysElapsed;
        return today.AddDays((int)Math.Ceiling((budget - spent) / perDay));
    }
}
