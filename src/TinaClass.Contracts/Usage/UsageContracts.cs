namespace TinaClass.Contracts.Usage;

/// <summary>The budget state (design 4.5): <c>ok</c>, then <c>warn_50</c>, <c>warn_80</c> and <c>over</c>; each month starts at <c>ok</c>.</summary>
public static class BudgetStates
{
    public const string Ok = "ok";
    public const string Warn50 = "warn_50";
    public const string Warn80 = "warn_80";
    public const string Over = "over";

    public static string For(long spent, long budget)
    {
        if (budget <= 0)
        {
            return Ok;
        }
        var percent = spent * 100 / budget;
        return percent >= 100 ? Over : percent >= 80 ? Warn80 : percent >= 50 ? Warn50 : Ok;
    }
}

public static class BudgetActions
{
    public const string Notify = "notify";
    public const string Block = "block";
}

public sealed record DailyUsageDto(DateOnly Day, long Cost, int Requests, long Tokens);

public sealed record ModelUsageDto(string ModelName, long Cost, long Tokens, int Requests);

public sealed record MemberUsageDto(Guid? UserId, string Name, string Role, long Cost, long Tokens, int Requests);

public sealed record UsageSummaryDto(DateOnly From, DateOnly To, long Total, long Budget, long Tokens, int Requests, int AvgFirstTokenMs, int CachePercent, string State,
    DateOnly? ProjectedHitDate, IReadOnlyList<DailyUsageDto> Days, IReadOnlyList<ModelUsageDto> Models, IReadOnlyList<MemberUsageDto> Members);

public sealed record UsageRecordDto(Guid Id, Guid? UserId, string UserName, string ModelName, string SubjectKind, int TokensIn, int TokensOut, int TokensCache, int FirstTokenMs, int TotalMs,
    long Cost, DateTimeOffset At);

public sealed record BudgetDto(long Amount, int[] WarnAtPercents, string Action, long Spent, string State, long PersonalAmount, int Version);

public sealed record UpdateBudgetRequest(long Amount, string Action, long PersonalAmount, int Version);

public static class UsageEvents
{
    public const string ThresholdCrossed = "budget.threshold_crossed";
}
