using Avalonia.Headless.XUnit;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Agents;
using TinaClass.Contracts.Agents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TinaClass.App.Tests.Features.Agents;

public class AgentsTests
{
    private static AgentsViewModel Open(string language = "en-US")
    {
        Ui.Open(language);
        Ui.Go(AppPages.Agents);
        var agents = Ui.Service<AgentsViewModel>();
        Assert.True(Ui.Until(() => agents.Run is not null));
        return agents;
    }

    [AvaloniaFact]
    public void The_page_lists_the_six_agents_and_opens_the_one_with_a_run_that_waits()
    {
        var agents = Open();

        Assert.Equal(AgentsSample.Agents.Count, agents.Items.Count);
        Assert.Equal(6, agents.Items.Count);
        Assert.Equal(AgentsSample.Agents[1].Id, agents.Selected?.Id);
        Assert.True(agents.Selected!.HasLive);
        Assert.Equal(RunStates.WaitingApproval, agents.Run!.State);
        // Run 842: seven steps, the last one waiting for the approval (design 16).
        Assert.Equal(AgentsSample.Run842.Count, agents.Run.Steps.Count);
        Assert.True(agents.HasRun);
        Assert.False(agents.HasNoRun);
    }

    [AvaloniaFact]
    public void The_agent_shows_its_tools_with_the_risk_and_when_it_asks_and_its_facts()
    {
        var agents = Open();

        Assert.Equal(agents.Selected!.Dto.Tools.Count, agents.Tools.Count);
        Assert.Contains(agents.Tools, t => t.Risk.Length > 0 && t.Approval.Length > 0);
        Assert.Equal(4, agents.Facts.Count);
        Assert.Equal(agents.Selected.Dto.ModelCode, agents.Facts[0].Value);
        // Every agent has earlier runs.
        Assert.True(agents.HasPastRuns);
    }

    [AvaloniaFact]
    public void The_servers_list_shows_their_state()
    {
        var agents = Open();

        Assert.Equal(AgentsSample.Servers.Count, agents.Servers.Count);
        Assert.Contains(agents.Servers, s => s.Tone == TinaClass.App.Common.Ui.StatusTone.Success);
    }

    [AvaloniaFact]
    public void Choosing_another_agent_shows_its_last_run_and_no_live_state()
    {
        var agents = Open();

        agents.Selected = agents.Items[0];
        Assert.True(Ui.Until(() => agents.Run is not null && agents.Run.AgentId == agents.Items[0].Id));
        Assert.Equal(RunStates.Succeeded, agents.Run!.State);
        Assert.False(agents.Items[0].HasLive);
    }

    [AvaloniaFact]
    public async Task One_approval_waits_and_the_sidebar_counts_it_until_it_is_decided()
    {
        var agents = Open();
        var badge = Ui.Service<IEnumerable<TinaClass.App.Common.Navigation.IBadgeSource>>().Single(b => b.PageKey == AppPages.Agents);
        await ((AgentsBadge)badge).RefreshAsync();

        Assert.True(agents.HasPending);
        Assert.Equal("1", badge.Text);

        var pending = (await agents.Service.PendingApprovalsAsync()).Single();
        Assert.Equal(ToolRisks.External, pending.Risk);
        await agents.Service.DecideAsync(pending.Id, approve: true, reason: null);
        Assert.True(Ui.Until(() => !agents.HasPending));
        Assert.True(Ui.Until(() => badge.Text is null));
        Assert.True(Ui.Until(() => agents.Run?.State == RunStates.Succeeded));
        // The send step was added after the approval.
        Assert.Equal(AgentsSample.Run842.Count + 1, agents.Run!.Steps.Count);
    }

    [AvaloniaFact]
    public async Task Rejecting_the_approval_stops_the_run_with_the_reason()
    {
        var agents = Open();
        var pending = (await agents.Service.PendingApprovalsAsync()).Single();

        await agents.Service.DecideAsync(pending.Id, approve: false, reason: "not now");

        Assert.True(Ui.Until(() => agents.Run?.State == RunStates.Failed));
        Assert.Contains("not now", agents.Run!.Steps[^1].Detail);
    }

    [AvaloniaFact]
    public async Task Running_an_agent_shows_the_new_run_with_its_three_steps()
    {
        var agents = Open();
        agents.Selected = agents.Items[0];
        Assert.True(Ui.Until(() => agents.Run?.AgentId == agents.Items[0].Id));

        await agents.StartAsync("Summarize this week's tickets");

        Assert.Equal("Summarize this week's tickets", agents.RunSubject);
        Assert.Equal(RunStates.Succeeded, agents.Run!.State);
        Assert.Equal(3, agents.Run.Steps.Count);
        Assert.Equal(agents.Run.Steps.Select(s => s.Ordinal).Order(), agents.Run.Steps.Select(s => s.Ordinal));
    }

    [AvaloniaFact]
    public void A_run_that_is_going_gets_a_step_at_a_time()
    {
        var agents = Open();
        TinaClass.App.Features.Agents.SampleAgentsService.AutoAdvance = true;
        try
        {
            agents.Selected = agents.Items[0];
            Assert.True(Ui.Until(() => agents.Run?.AgentId == agents.Items[0].Id));
            _ = agents.StartAsync("A task");
            Assert.True(Ui.Until(() => agents.Run?.State == RunStates.Running && agents.Run.Steps.Count == 1));
        }
        finally
        {
            TinaClass.App.Features.Agents.SampleAgentsService.AutoAdvance = false;
        }
    }

    [AvaloniaFact]
    public void The_words_follow_the_language()
    {
        var agents = Open();
        var english = agents.RunState;

        Ui.Shell.SetLanguageCommand.Execute("zh-CN");
        Ui.Settle(6);

        Assert.NotEqual(english, agents.RunState);
        Assert.Matches("[一-鿿]", agents.PendingText);
    }
}
