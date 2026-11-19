using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Usage.Localization;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Usage;
using TKCode.Arc;

namespace TinaClass.App.Features.Usage;

/// <summary>A row of the by-model split: the name, the money, and the share as a percentage for the bar.</summary>
public sealed record ModelShareRow(string Name, string Cost, double Percent, string Detail);

/// <summary>A row of the by-member table.</summary>
public sealed record MemberRow(string Name, string Role, string Cost, string Requests, string Tokens, string Share);

/// <summary>The usage page: four figures, the budget, a bar per day, and the splits by model and by member for the chosen range.</summary>
public sealed partial class UsageViewModel(IUsageService service) : PageViewModel
{
    /// <summary>0 is the current month (all of the sample days), 1 the last seven days.</summary>
    [ObservableProperty]
    private int _rangeIndex;

    [ObservableProperty]
    private double _totalValue;

    [ObservableProperty]
    private double _tokensValue;

    [ObservableProperty]
    private double _requestsValue;

    [ObservableProperty]
    private double _firstTokenValue;

    [ObservableProperty]
    private string _cacheText = string.Empty;

    [ObservableProperty]
    private double _budgetSpent;

    [ObservableProperty]
    private double _budgetLimit = 1;

    [ObservableProperty]
    private string _budgetText = string.Empty;

    [ObservableProperty]
    private string _budgetNote = string.Empty;

    [ObservableProperty]
    private string _stateText = string.Empty;

    [ObservableProperty]
    private BadgeTone _stateTone;

    [ObservableProperty]
    private bool _hasData;

    public IReadOnlyList<ChartPoint> Days { get; private set; } = [];

    public ObservableCollection<ModelShareRow> Models { get; } = [];

    public ObservableCollection<MemberRow> Members { get; } = [];

    private UsageSummaryDto? _summary;
    private BudgetDto? _budget;

    /// <summary>The range as dates, ending today (the API's clock is the server's; the sample's is the sample today).</summary>
    private (DateOnly From, DateOnly To) Range()
    {
        var today = DateOnly.FromDateTime(Common.Sample.SampleClock.Now.LocalDateTime);
        var first = RangeIndex == 1 ? today.AddDays(-6) : new DateOnly(today.Year, today.Month, 1);
        return (first, today);
    }

    partial void OnRangeIndexChanged(int value)
    {
        if (IsLoaded)
        {
            _ = RefreshAsync();
        }
    }

    private bool IsLoaded => _summary is not null || _budget is not null;

    protected override async Task LoadAsync()
    {
        try
        {
            var (from, to) = Range();
            _budget = await service.BudgetAsync();
            _summary = await service.SummaryAsync(from, to);
            Apply();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Moves the range by one step with the <c>[</c> and <c>]</c> keys.</summary>
    [RelayCommand]
    private void StepRange(string direction) => RangeIndex = Math.Clamp(RangeIndex + (direction == "next" ? 1 : -1), 0, 1);

    private void Apply()
    {
        if (_summary is not { } s || _budget is not { } b)
        {
            return;
        }
        HasData = s.Requests > 0;
        TotalValue = s.Total / 100.0;
        TokensValue = Math.Round(s.Tokens / 1_000_000.0, 1);
        RequestsValue = s.Requests;
        FirstTokenValue = s.AvgFirstTokenMs / 1000.0;
        CacheText = UsageLangResources.Format(UsageLangResourceKind.CacheHit, s.CachePercent);
        BudgetLimit = Math.Max(b.Amount / 100.0, 1);
        BudgetSpent = b.Spent / 100.0;
        BudgetText = UsageLangResources.Format(UsageLangResourceKind.BudgetUsed, Money.Format(b.Spent), Money.Format(b.Amount, 0));
        BudgetNote = s.ProjectedHitDate is { } hit
            ? UsageLangResources.Format(UsageLangResourceKind.Projected, hit.ToString("M", Text.Culture))
            : UsageLangResources.Get(UsageLangResourceKind.Lasts);
        (StateText, StateTone) = b.State switch
        {
            BudgetStates.Warn50 => (UsageLangResources.Get(UsageLangResourceKind.StateWarn50), BadgeTone.Info),
            BudgetStates.Warn80 => (UsageLangResources.Get(UsageLangResourceKind.StateWarn80), BadgeTone.Warning),
            BudgetStates.Over => (UsageLangResources.Get(UsageLangResourceKind.StateOver), BadgeTone.Danger),
            _ => (UsageLangResources.Get(UsageLangResourceKind.StateOk), BadgeTone.Success),
        };
        // Weekdays use the foreground, weekends 35% of it (the chart is one colour, design 10).
        Days = [.. s.Days.Select(d => new ChartPoint(d.Day.ToString("M/d", Text.Culture), d.Cost / 100.0)
        {
            Label = $"{d.Day.ToString("M/d", Text.Culture)}: {Money.Format(d.Cost)}",
            Color = d.Day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? Color.FromArgb(0x59, 0x8C, 0x8C, 0x94) : null,
        })];
        OnPropertyChanged(nameof(Days));
        var max = Math.Max(s.Models.Sum(m => m.Cost), 1);
        Models.Clear();
        foreach (var m in s.Models)
        {
            Models.Add(new ModelShareRow(m.ModelName, Money.Format(m.Cost), m.Cost * 100.0 / max, string.Format(Text.Culture, "{0:N0}", m.Requests)));
        }
        var total = Math.Max(s.Total, 1);
        Members.Clear();
        foreach (var m in s.Members)
        {
            Members.Add(new MemberRow(m.UserId is null ? UsageLangResources.Get(UsageLangResourceKind.AutoRuns) : m.Name, RoleName(m.Role), Money.Format(m.Cost),
                string.Format(Text.Culture, "{0:N0}", m.Requests), (m.Tokens / 1_000_000.0).ToString("0.0", Text.Culture) + "M", (m.Cost * 100.0 / total).ToString("0", Text.Culture) + "%"));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string RoleName(string role) => UsageLangResources.Get(role switch
    {
        "owner" => UsageLangResourceKind.RoleOwner,
        "admin" => UsageLangResourceKind.RoleAdmin,
        "viewer" => UsageLangResourceKind.RoleViewer,
        "agent" => UsageLangResourceKind.RoleAgent,
        _ => UsageLangResourceKind.RoleMember,
    });

    /// <summary>Raised after the figures were applied (the page redraws its chart and meter).</summary>
    public event EventHandler? Changed;

    /// <summary>The words follow the language.</summary>
    public void RefreshTexts() => Apply();
}
