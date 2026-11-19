using TinaClass.App.Common.Api;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Usage;

namespace TinaClass.App.Features.Usage.Api;

/// <summary>The usage of the API (<c>/usage/summary</c>, <c>/usage/budget</c>).</summary>
public sealed class ApiUsageService(ApiClient api) : IUsageService
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<UsageSummaryDto> SummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellation = default) =>
        api.GetAsync($"{ApiRoutes.Usage}/summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", Json.UsageSummaryDto, cancellation);

    public Task<BudgetDto> BudgetAsync(CancellationToken cancellation = default) =>
        api.GetAsync($"{ApiRoutes.Usage}/budget", Json.BudgetDto, cancellation);
}
