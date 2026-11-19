using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TinaClass.App.Common.Services;
using TinaClass.App.Features.Flow.Localization;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Workflows;
using Microsoft.Extensions.DependencyInjection;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Flow;

/// <summary>
/// The workflow page. Arc's <c>WorkflowCanvas</c> shows the draft (a node per <see cref="FlowNodeDto"/>, a labeled output for each answer of a branch) and
/// can be edited (move, connect, delete; Cmd/Ctrl+S saves the draft). The latest run's states color the nodes, and its edges draw by themselves.
/// </summary>
public partial class FlowPage : UserControl
{
    private FlowViewModel? _viewModel;
    private bool _building;
    private bool _fitPending;

    public FlowPage()
    {
        InitializeComponent();
        Zoom.Target = Canvas;
        Minimap.Target = Canvas;
        Canvas.GraphEdited += (_, _) =>
        {
            if (!_building)
            {
                _viewModel?.MarkChanged();
            }
        };
        Canvas.SizeChanged += (_, _) => TryFit();
        ArcLanguage.Changed += (_, _) => _viewModel?.RefreshTexts();
    }

    /// <summary>Shows the whole graph once the canvas has a size (a graph that is loaded before the page is laid out waits for it).</summary>
    private void TryFit()
    {
        if (_fitPending && Canvas.Bounds.Width > 0 && Canvas.Bounds.Height > 0)
        {
            _fitPending = false;
            Canvas.FitToContent(false);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.S && e.KeyModifiers is KeyModifiers.Meta or KeyModifiers.Control && _viewModel is { } vm)
        {
            _ = vm.SaveAsync();
            e.Handled = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is FlowViewModel vm && vm != _viewModel)
        {
            _viewModel = vm;
            vm.GraphProvider = ToDto;
            vm.GraphLoaded += (_, _) => Build(vm.Flow?.Draft);
            vm.RunChanged += (_, _) => Color(vm.Run);
            vm.StartRequested += async (_, _) => await StartAsync(vm);
            vm.ReviewRequested += async (_, _) => await DesktopApp.Services.GetRequiredService<IApprovalReview>().ReviewAsync(this);
        }
    }

    private async Task StartAsync(FlowViewModel vm)
    {
        var input = new Input { Label = FlowLangResources.Get(FlowLangResourceKind.RunInput), Text = FlowLangResources.Get(FlowLangResourceKind.RunInputDefault) };
        var result = await Dialog.ShowAsync(this, FlowLangResources.Get(FlowLangResourceKind.RunTitle), new StackPanel { MinWidth = 380, Children = { input } }, null,
            new DialogAction(FlowLangResources.Get(FlowLangResourceKind.Cancel), false, ButtonVariant.Secondary),
            new DialogAction(FlowLangResources.Get(FlowLangResourceKind.Start), true, ButtonVariant.Primary));
        if (result is true)
        {
            await vm.StartAsync(input.Text?.Trim());
        }
    }

    /// <summary>Builds the graph of the canvas from the draft: a node per node, the branch's answers as named outputs.</summary>
    private void Build(FlowGraphDto? draft)
    {
        _building = true;
        var graph = new WorkflowGraph();
        if (draft is not null)
        {
            foreach (var n in draft.Nodes)
            {
                var node = new WorkflowNode(n.Key, n.Title)
                {
                    Description = n.Subtitle,
                    Kind = n.Kind,
                    Position = new Point(n.X, n.Y),
                    // A little narrower than Arc's 300 px card, so that a pipeline of five columns fits the page with room between the cards.
                    Size = new Size(236, 60),
                    Icon = Lucide.Geometry(IconOf(n.Kind)),
                    HasDefaultInput = n.Kind != NodeKinds.Trigger,
                };
                foreach (var label in draft.Edges.Where(e => e.From == n.Key && e.Label is not null).Select(e => e.Label!).Distinct())
                {
                    node.Outputs.Add(new WorkflowPort(WorkflowPortDirection.Output, label));
                }
                graph.Nodes.Add(node);
            }
            foreach (var e in draft.Edges)
            {
                var source = graph.FindNode(e.From);
                var target = graph.FindNode(e.To);
                if (source is not null && target is not null)
                {
                    graph.Connect(source, e.Label is null ? null : source.Outputs.FirstOrDefault(p => p.Label == e.Label), target, null);
                }
            }
        }
        Canvas.Graph = graph;
        _building = false;
        Color(_viewModel?.Run);
        // Fitting needs the canvas' size: right away when it has one, else when it gets one (see the constructor).
        _fitPending = true;
        TryFit();
    }

    /// <summary>The nodes take the states of the run: done, running (a node that waits for approval is running), failed, skipped, or not reached.</summary>
    private void Color(FlowRunDto? run)
    {
        foreach (var node in Canvas.Graph?.Nodes ?? [])
        {
            var step = run?.Nodes.FirstOrDefault(n => n.Key == node.Id);
            node.State = step?.State switch
            {
                NodeStates.Succeeded => WorkflowNodeState.Done,
                NodeStates.Running or NodeStates.Waiting => WorkflowNodeState.Running,
                NodeStates.Failed => WorkflowNodeState.Failed,
                NodeStates.Skipped => WorkflowNodeState.Skipped,
                NodeStates.Pending => WorkflowNodeState.Pending,
                _ => WorkflowNodeState.Idle,
            };
            node.Duration = step is { DurationMs: > 0 } ? TimeSpan.FromMilliseconds(step.DurationMs) : null;
            node.Result = step?.Output;
        }
    }

    /// <summary>The graph of the canvas as the DTO of the API (positions and the labels of the answers are kept).</summary>
    private FlowGraphDto ToDto()
    {
        var graph = Canvas.Graph;
        if (graph is null)
        {
            return new FlowGraphDto([], []);
        }
        return new FlowGraphDto(
            [.. graph.Nodes.Select(n => new FlowNodeDto(n.Id, n.Kind ?? NodeKinds.Tool, n.Title, n.Description ?? string.Empty, (int)n.Position.X, (int)n.Position.Y))],
            [.. graph.Edges.Select(e => new FlowEdgeDto(e.Source.Id, e.Target.Id, e.SourcePort?.Label))]);
    }

    private static LucideIconKind IconOf(string kind) => kind switch
    {
        NodeKinds.Trigger => LucideIconKind.Zap,
        NodeKinds.Model => LucideIconKind.Sparkles,
        NodeKinds.Retrieve => LucideIconKind.BookOpen,
        NodeKinds.Branch => LucideIconKind.GitBranch,
        NodeKinds.Approval => LucideIconKind.ShieldCheck,
        NodeKinds.Send => LucideIconKind.Send,
        _ => LucideIconKind.Wrench,
    };
}
