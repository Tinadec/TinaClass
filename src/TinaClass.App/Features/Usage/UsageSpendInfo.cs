using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Services;

namespace TinaClass.App.Features.Usage;

/// <summary>What the Usage feature tells the sidebar's footer: this month's spend against the budget (it replaces the default that shows nothing).</summary>
public sealed class UsageSpendInfo(IUsageService usage) : ISpendInfo
{
    public async Task<SpendSnapshot?> CurrentAsync(CancellationToken cancellation = default)
    {
        try
        {
            var budget = await usage.BudgetAsync(cancellation);
            return new SpendSnapshot(budget.Spent, budget.Amount, Common.Sample.SampleClock.Now.ToString("MMM", Text.Culture));
        }
        catch (Common.Api.ServiceException)
        {
            // The footer is a nicety: without the budget (no permission, no network) it shows nothing.
            return null;
        }
    }
}
