using TinaClass.Contracts.Usage;

namespace TinaClass.App.Features.Usage;

/// <summary>
/// Where the app gets its spend. The sample service derives it from the sample month (Contracts/Usage/UsageSample); <c>Api/ApiUsageService.cs</c>
/// reads <c>/usage</c>. The budget is the workspace's monthly amount; every amount is whole minor units (fen).
/// </summary>
public interface IUsageService
{
    /// <summary>The totals, the daily series and the splits by model and member for the days from <paramref name="from"/> to <paramref name="to"/>.</summary>
    Task<UsageSummaryDto> SummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellation = default);

    /// <summary>The month's budget with what has been spent so far.</summary>
    Task<BudgetDto> BudgetAsync(CancellationToken cancellation = default);
}
