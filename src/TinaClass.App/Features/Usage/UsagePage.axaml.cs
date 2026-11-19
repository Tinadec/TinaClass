using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TinaClass.App.Features.Usage.Localization;
using TKCode.Arc;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Usage;

/// <summary>The usage page. Arc's chart, meter, cards and table are filled here from the view model each time its figures change.</summary>
public partial class UsagePage : UserControl
{
    private UsageViewModel? _viewModel;

    public UsagePage()
    {
        InitializeComponent();
        ArcLanguage.Changed += (_, _) =>
        {
            Fill();
            _viewModel?.RefreshTexts();
        };
        Fill();
    }

    /// <summary><c>[</c> and <c>]</c> move between the ranges (design 8).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.OemOpenBrackets or Key.OemCloseBrackets && _viewModel is { } vm)
        {
            vm.StepRangeCommand.Execute(e.Key == Key.OemOpenBrackets ? "previous" : "next");
            e.Handled = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is UsageViewModel vm && vm != _viewModel)
        {
            _viewModel = vm;
            vm.Changed += (_, _) => Redraw();
            Redraw();
        }
    }

    private void Fill()
    {
        TotalCard.Label = UsageLangResources.Get(UsageLangResourceKind.Total);
        TokensCard.Label = UsageLangResources.Get(UsageLangResourceKind.Tokens);
        RequestsCard.Label = UsageLangResources.Get(UsageLangResourceKind.Requests);
        FirstTokenCard.Label = UsageLangResources.Get(UsageLangResourceKind.FirstToken);
        Range.Items.Clear();
        Range.Items.Add(new SegmentedOption(0, UsageLangResources.Get(UsageLangResourceKind.RangeMonth)));
        Range.Items.Add(new SegmentedOption(1, UsageLangResources.Get(UsageLangResourceKind.Range7)));
        Range.SelectedIndex = _viewModel?.RangeIndex ?? 0;
        MemberTable.Columns.Clear();
        MemberTable.Columns.Add(new DataTableColumn(UsageLangResources.Get(UsageLangResourceKind.ColMember), o => ((MemberRow)o).Name) { Width = new GridLength(2, GridUnitType.Star), IsNumeric = false });
        MemberTable.Columns.Add(new DataTableColumn(UsageLangResources.Get(UsageLangResourceKind.ColCost), o => ((MemberRow)o).Cost) { Width = new GridLength(1.2, GridUnitType.Star) });
        MemberTable.Columns.Add(new DataTableColumn(UsageLangResources.Get(UsageLangResourceKind.ColRequests), o => ((MemberRow)o).Requests) { Width = new GridLength(1, GridUnitType.Star) });
        MemberTable.Columns.Add(new DataTableColumn(UsageLangResources.Get(UsageLangResourceKind.ColShare), o => ((MemberRow)o).Share) { Width = new GridLength(0.8, GridUnitType.Star) });
        Chart.Label = UsageLangResources.Get(UsageLangResourceKind.Daily);
        Chart.AverageLabel = UsageLangResources.Get(UsageLangResourceKind.DailyAverage);
        // The unit follows the currency option: 元 or CNY for yuan, the code for the others.
        var unit = TinaClass.Contracts.Common.Money.Code == "CNY" ? UsageLangResources.Get(UsageLangResourceKind.CurrencyUnit) : TinaClass.Contracts.Common.Money.Code;
        Chart.Unit = unit;
        Meter.Unit = unit;
        TotalCard.Prefix = TinaClass.Contracts.Common.Money.Symbol;
        Meter.FreeLabel = UsageLangResources.Get(UsageLangResourceKind.MeterFree);
        Meter.OverLabel = UsageLangResources.Get(UsageLangResourceKind.MeterOver);
    }

    private void Redraw()
    {
        if (_viewModel is not { } vm)
        {
            return;
        }
        Chart.XSelector = p => ((ChartPoint)p).X;
        Chart.YSelector = p => ((ChartPoint)p).Value;
        Chart.XAxisFormatter = p => p is ChartPoint point ? point.X?.ToString() ?? string.Empty : string.Empty;
        Chart.ItemsSource = vm.Days;
        Meter.Segments.Clear();
        Meter.Segments.Add(new UsageSegment("spent", UsageLangResources.Get(UsageLangResourceKind.Budget), vm.BudgetSpent));
        // The panel above already names the budget.
        Meter.Label = string.Empty;
    }
}
