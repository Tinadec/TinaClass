using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Shell;
using TinaClass.App.Features.Agents;
using TinaClass.App.Features.Flow;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Workflows;
using TKCode.Arc;
using Xunit;

namespace TinaClass.App.Tests.Features.Flow;

public class FlowTests
{
    private static (MainWindow Window, FlowViewModel Flow) Open(string language = "en-US")
    {
        var window = Ui.Open(language);
        Ui.Go(AppPages.Flow);
        var flow = Ui.Service<FlowViewModel>();
        Assert.True(Ui.Until(() => flow.Run is not null));
        return (window, flow);
    }

    [AvaloniaFact]
    public void The_page_opens_the_flow_v12_with_run_842_waiting_at_the_approval()
    {
        var (_, flow) = Open();

        Assert.Equal("工单自动分诊", flow.Flow!.Name);
        Assert.Equal(12, flow.Flow.PublishedVersion);
        Assert.Equal(WorkflowsSample.Graph.Nodes.Count, flow.Flow.Draft.Nodes.Count);
        Assert.Equal(RunStates.WaitingApproval, flow.Run!.State);
        Assert.True(flow.IsWaiting);
        Assert.Equal(842, flow.Run.Ordinal);
        Assert.Equal(WorkflowsSample.Run842.Count, flow.Trace.Count);
    }

    [AvaloniaFact]
    public void The_trace_strip_has_a_segment_for_each_node_with_the_state_as_the_tone()
    {
        var (_, flow) = Open();

        var tones = flow.Trace.Select(t => t.Tone).ToList();
        Assert.Equal(5, tones.Count(t => t == TinaClass.App.Common.Ui.StatusTone.Success));
        Assert.Equal(1, tones.Count(t => t == TinaClass.App.Common.Ui.StatusTone.Warning));
        // A node that was not reached (or skipped) is drawn hollow.
        Assert.Equal(2, flow.Trace.Count(t => t.IsHollow));
    }

    [AvaloniaFact]
    public void The_canvas_shows_the_nodes_the_edges_and_the_colors_of_the_run()
    {
        var (window, _) = Open();

        var canvas = Ui.Pages(window).View!.GetVisualDescendants().OfType<WorkflowCanvas>().Single();
        Assert.NotNull(canvas.Graph);
        Assert.Equal(8, canvas.Graph!.Nodes.Count);
        Assert.Equal(7, canvas.Graph.Edges.Count);
        Assert.Equal(WorkflowNodeState.Done, canvas.Graph.FindNode("n1")!.State);
        Assert.Equal(WorkflowNodeState.Running, canvas.Graph.FindNode("n6")!.State);
        Assert.Equal(WorkflowNodeState.Skipped, canvas.Graph.FindNode("n8")!.State);
        // The branch has two labeled answers.
        var answers = canvas.Graph.Edges.Where(e => e.Source.Id == "n3").Select(e => e.SourcePort?.Label).ToList();
        Assert.Contains("是", answers);
        Assert.Contains("否", answers);
    }

    [AvaloniaFact]
    public async Task Deciding_the_approval_finishes_the_waiting_run()
    {
        var (_, flow) = Open();
        var agents = Ui.Service<IAgentsService>();
        var pending = (await agents.PendingApprovalsAsync()).Single();

        await agents.DecideAsync(pending.Id, true, null);

        Assert.True(Ui.Until(() => flow.Run?.State == RunStates.Succeeded));
        Assert.False(flow.IsWaiting);
        Assert.All(flow.Run!.Nodes.Where(n => n.Key != "n8"), n => Assert.Equal(NodeStates.Succeeded, n.State));
    }

    [AvaloniaFact]
    public async Task Save_and_publish_move_the_draft_and_the_version()
    {
        var (_, flow) = Open();
        Assert.False(flow.HasChanges);

        flow.MarkChanged();
        Assert.True(flow.HasChanges);
        Assert.Contains(flow.Actions, a => a.IsPrimary);

        await flow.SaveAsync();
        Assert.True(flow.HasChanges);
        flow.PublishCommand.Execute(null);
        Assert.True(Ui.Until(() => !flow.HasChanges));
        Assert.Equal(13, flow.Flow!.PublishedVersion);
    }

    [AvaloniaFact]
    public async Task Running_the_flow_adds_a_complete_run()
    {
        var (_, flow) = Open();

        await flow.StartAsync("ticket 9");

        Assert.Equal(RunStates.Succeeded, flow.Run!.State);
        Assert.Equal(843, flow.Run.Ordinal);
        Assert.False(flow.IsWaiting);
        Assert.Contains("#843", flow.RunLabel);
    }

    [AvaloniaFact]
    public void A_run_in_progress_moves_the_nodes_along()
    {
        var (_, flow) = Open();
        SampleFlowService.AutoAdvance = true;
        try
        {
            _ = flow.StartAsync(null);
            Assert.True(Ui.Until(() => flow.Run?.State == RunStates.Running && flow.Run.Nodes.Any(n => n.State == NodeStates.Running)));
            Assert.Contains(flow.Run!.Nodes, n => n.State == NodeStates.Pending);
        }
        finally
        {
            SampleFlowService.AutoAdvance = false;
        }
    }

    [AvaloniaFact]
    public void The_words_follow_the_language()
    {
        var (_, flow) = Open();
        var english = flow.RunState;

        Ui.Shell.SetLanguageCommand.Execute("zh-TW");
        Ui.Settle(6);

        Assert.NotEqual(english, flow.RunState);
        Assert.Matches("[一-鿿]", flow.RunState);
    }
}
