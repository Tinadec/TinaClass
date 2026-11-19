using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Shell;
using TinaClass.App.Features.Usage;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Usage;
using TKCode.Arc;
using Xunit;

namespace TinaClass.App.Tests.Features.Usage;

public class UsageTests
{
    private static (MainWindow Window, UsageViewModel Usage) Open(string language = "en-US")
    {
        var window = Ui.Open(language);
        Ui.Go(AppPages.Usage);
        var usage = Ui.Service<UsageViewModel>();
        Assert.True(Ui.Until(() => usage.HasData));
        return (window, usage);
    }

    [AvaloniaFact]
    public void This_month_shows_the_sample_figures_and_the_budget()
    {
        var (_, usage) = Open();

        Assert.Equal(0, usage.RangeIndex);
        Assert.Equal(1284.60, usage.TotalValue, 2);
        Assert.Equal(1284.60, usage.BudgetSpent, 2);
        Assert.Equal(2000, usage.BudgetLimit);
        Assert.Equal(14, usage.Days.Count);
        Assert.Contains("1,284.60", usage.BudgetText);
        // 64% of the budget is used: the first warning.
        Assert.Equal(BadgeTone.Info, usage.StateTone);
        Assert.False(string.IsNullOrEmpty(usage.BudgetNote));
    }

    [AvaloniaFact]
    public void The_models_and_the_members_add_up_to_the_total_and_are_sorted_by_cost()
    {
        var (_, usage) = Open();

        Assert.Equal(4, usage.Models.Count);
        Assert.Equal(6, usage.Members.Count);
        Assert.Equal(100, usage.Models.Sum(m => m.Percent), 6);
        Assert.Equal(usage.Models.Max(m => m.Percent), usage.Models[0].Percent);
        // The agents running on their own are the last row, with their own name.
        Assert.Equal("Agents running on their own", usage.Members[^1].Name);
    }

    [AvaloniaFact]
    public void The_seven_day_range_takes_the_last_seven_days_of_the_same_rows()
    {
        var (_, usage) = Open();
        var month = usage.TotalValue;

        usage.RangeIndex = 1;

        Assert.True(Ui.Until(() => usage.Days.Count == 7));
        Assert.True(usage.TotalValue < month);
        // 14,860 + 3,630 + 14,120 + 10,980 + 12,640 + 11,870 + 1,690 fen.
        Assert.Equal(697.90, usage.TotalValue, 2);
        // The budget meter is about the month, whatever the range.
        Assert.Equal(1284.60, usage.BudgetSpent, 2);
    }

    [AvaloniaFact]
    public void The_bracket_keys_step_the_range()
    {
        var (_, usage) = Open();

        usage.StepRangeCommand.Execute("next");
        Assert.Equal(1, usage.RangeIndex);
        usage.StepRangeCommand.Execute("next");
        Assert.Equal(1, usage.RangeIndex);
        usage.StepRangeCommand.Execute("prev");
        Assert.Equal(0, usage.RangeIndex);
    }

    [AvaloniaFact]
    public void The_page_draws_the_chart_the_meter_and_the_cards_and_the_money_follows_the_currency()
    {
        var (window, _) = Open();

        var page = Ui.Pages(window).View!;
        var chart = page.GetVisualDescendants().OfType<BarChart>().Single();
        Assert.Equal(14, chart.ItemsSource!.Cast<object>().Count());
        var meter = page.GetVisualDescendants().OfType<UsageMeter>().Single();
        Assert.Equal(2000, meter.Limit);
        Assert.Equal(1284.60, meter.Segments.Sum(x => x.Value), 2);
        Assert.Equal("¥", page.GetVisualDescendants().OfType<MetricCard>().First().Prefix);
        Assert.Equal(chart.Unit, meter.Unit);
        Assert.False(string.IsNullOrEmpty(meter.Unit));
    }

    [AvaloniaFact]
    public void The_sidebar_footer_shows_the_spend_of_the_month()
    {
        var window = Ui.Open();
        Ui.Go(AppPages.Usage);

        Assert.True(Ui.Until(() => Ui.Shell.HasSpend));
        Assert.Equal(128_460, Ui.Shell.SpendValue);
        Assert.Equal(200_000, Ui.Shell.SpendLimit);
        Assert.Equal("¥1,284.60", Ui.Shell.SpendText);
    }

    [AvaloniaFact]
    public void The_money_is_formatted_in_the_configured_currency()
    {
        try
        {
            Money.Configure("USD");
            Assert.Equal("$", Money.Symbol);
            Assert.Equal("USD", Money.Code);
            Assert.Equal("$12.50", Money.Format(1250));
            Money.Configure("CNY");
            Assert.Equal("¥12.50", Money.Format(1250));
        }
        finally
        {
            Money.Configure("CNY");
        }
    }

    [AvaloniaFact]
    public void The_words_follow_the_language()
    {
        var (_, usage) = Open();
        var english = usage.StateText;

        Ui.Shell.SetLanguageCommand.Execute("zh-CN");
        Ui.Settle(6);

        Assert.NotEqual(english, usage.StateText);
        Assert.Matches("[一-鿿]", usage.BudgetNote);
    }
}
