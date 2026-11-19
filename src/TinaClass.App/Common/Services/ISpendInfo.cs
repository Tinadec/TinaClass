namespace TinaClass.App.Common.Services;

/// <summary>This month's spend against the budget, for the sidebar's footer. Amounts are minor units (fen).</summary>
public sealed record SpendSnapshot(long Spent, long Limit, string MonthLabel);

/// <summary>
/// What the sidebar footer shows about spending. The Usage feature provides the real numbers (and replaces this default when it is
/// left in); without it the footer shows nothing.
/// </summary>
public interface ISpendInfo
{
    Task<SpendSnapshot?> CurrentAsync(CancellationToken cancellation = default);
}

/// <summary>The default: no spend information (the Usage feature is not in this app).</summary>
public sealed class NoSpendInfo : ISpendInfo
{
    public Task<SpendSnapshot?> CurrentAsync(CancellationToken cancellation = default) => Task.FromResult<SpendSnapshot?>(null);
}
