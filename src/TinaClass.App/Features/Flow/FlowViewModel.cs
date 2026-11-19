using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Ui;
using TinaClass.App.Features.Agents;
using TinaClass.App.Features.Flow.Localization;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Workflows;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Features.Flow;

/// <summary>
/// The workflow page: the draft graph (the page puts it on the canvas when <see cref="GraphLoaded"/> is raised and reads the edited one through
/// <see cref="GraphProvider"/>), the version, and the latest run: its state, and the run trace (a <see cref="TraceSegment"/> per step). A run that goes is
/// read again every second; one that waits at the approval node continues when the approval is decided (agents' <see cref="IAgentsService.ApprovalsChanged"/>).
/// </summary>
public sealed partial class FlowViewModel(IFlowService service, IAgentsService agents) : PageViewModel
{
    [ObservableProperty]
    private FlowDto? _flow;

    [ObservableProperty]
    private FlowRunDto? _run;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _runLabel = string.Empty;

    [ObservableProperty]
    private string _runState = string.Empty;

    [ObservableProperty]
    private BadgeTone _runTone;

    [ObservableProperty]
    private bool _isWaiting;

    [ObservableProperty]
    private bool _hasChanges;

    [ObservableProperty]
    private IReadOnlyList<TraceSegment> _trace = [];

    public bool HasFlow => Flow is not null;

    public bool HasNoFlow => Flow is null && !IsLoading;

    public bool HasRun => Run is not null;

    public bool HasNoRun => Run is null && Flow is not null;

    public string VersionText => Flow is null ? string.Empty : FlowLangResources.Format(FlowLangResourceKind.Version, Flow.PublishedVersion);

    /// <summary>The page gives the graph as it is on the canvas now (null: use the saved draft).</summary>
    public Func<FlowGraphDto>? GraphProvider { get; set; }

    /// <summary>The draft was loaded or saved: the page builds the canvas from <see cref="Flow"/>.</summary>
    public event EventHandler? GraphLoaded;

    /// <summary>The run to show changed: the page colors the nodes.</summary>
    public event EventHandler? RunChanged;

    /// <summary>"Run": the page asks what to work on, then calls <see cref="StartAsync"/>.</summary>
    public event EventHandler? StartRequested;

    /// <summary>"Review": the page opens the approval dialog.</summary>
    public event EventHandler? ReviewRequested;

    private CancellationTokenSource? _poll;

    protected override async Task LoadAsync()
    {
        agents.ApprovalsChanged -= OnApprovalsChanged;
        agents.ApprovalsChanged += OnApprovalsChanged;
        try
        {
            Flow = await service.FlowAsync();
            Title = Flow?.Name ?? string.Empty;
            HasChanges = Flow?.HasChanges ?? false;
            OnPropertyChanged(nameof(HasFlow));
            OnPropertyChanged(nameof(HasNoFlow));
            OnPropertyChanged(nameof(VersionText));
            RebuildBar();
            GraphLoaded?.Invoke(this, EventArgs.Empty);
            if (Flow is { } flow)
            {
                var run = (await service.RunsAsync(flow.Id)).FirstOrDefault();
                SetRun(run);
                if (run is not null && run.State == RunStates.Running)
                {
                    _ = PollAsync(run.Id);
                }
            }
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    private async void OnApprovalsChanged(object? sender, EventArgs e)
    {
        if (Run is { } run && run.State == RunStates.WaitingApproval)
        {
            _poll?.Cancel();
            await PollAsync(run.Id, immediately: true);
        }
    }

    private void SetRun(FlowRunDto? run)
    {
        Run = run;
        IsWaiting = run?.State == RunStates.WaitingApproval;
        RunLabel = run is null ? string.Empty : FlowLangResources.Format(FlowLangResourceKind.RunLabel, run.Ordinal, run.FlowVersion);
        RunState = run is null ? string.Empty : FlowLangResources.Get(run.State switch
        {
            RunStates.Running => FlowLangResourceKind.StateRunning,
            RunStates.WaitingApproval => FlowLangResourceKind.StateWaiting,
            RunStates.Succeeded => FlowLangResourceKind.StateSucceeded,
            RunStates.Failed => FlowLangResourceKind.StateFailed,
            _ => FlowLangResourceKind.StateQueued,
        });
        RunTone = run?.State switch
        {
            RunStates.Running => BadgeTone.Info,
            RunStates.WaitingApproval or RunStates.Queued => BadgeTone.Warning,
            RunStates.Succeeded => BadgeTone.Success,
            RunStates.Failed => BadgeTone.Danger,
            _ => BadgeTone.Neutral,
        };
        Trace = run is null ? [] : TraceOf(run);
        OnPropertyChanged(nameof(HasRun));
        OnPropertyChanged(nameof(HasNoRun));
        RunChanged?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<TraceSegment> TraceOf(FlowRunDto run)
    {
        var titles = (Flow?.Draft.Nodes ?? []).ToDictionary(n => n.Key, n => n.Title);
        return
        [
            .. run.Nodes.Select(n => new TraceSegment(titles.GetValueOrDefault(n.Key, n.Key), n.DurationMs > 0 ? Seconds(n.DurationMs) : string.Empty,
                n.State switch
                {
                    NodeStates.Succeeded => StatusTone.Success,
                    NodeStates.Running => StatusTone.Running,
                    NodeStates.Waiting => StatusTone.Warning,
                    NodeStates.Failed => StatusTone.Danger,
                    _ => StatusTone.Muted,
                }, Math.Max(n.DurationMs, 400), IsHollow: n.State is NodeStates.Pending or NodeStates.Skipped)),
        ];
    }

    internal static string Seconds(int milliseconds) => milliseconds < 1000
        ? FlowLangResources.Format(FlowLangResourceKind.Millis, milliseconds)
        : FlowLangResources.Format(FlowLangResourceKind.Seconds, (milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture));

    private async Task PollAsync(Guid runId, bool immediately = false)
    {
        var source = _poll = new CancellationTokenSource();
        try
        {
            while (!source.IsCancellationRequested)
            {
                if (!immediately)
                {
                    await Task.Delay(1000, source.Token);
                }
                immediately = false;
                var run = await service.RunAsync(runId, source.Token);
                if (source.IsCancellationRequested)
                {
                    return;
                }
                SetRun(run);
                if (run.State is not (RunStates.Running or RunStates.Queued))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A newer run took over.
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>The canvas was edited.</summary>
    public void MarkChanged()
    {
        HasChanges = true;
        RebuildBar();
    }

    private void RebuildBar()
    {
        Crumbs = Flow is null ? [] : [Flow.Name];
        BarNote = Flow is null ? null : HasChanges ? FlowLangResources.Get(FlowLangResourceKind.Draft) : FlowLangResources.Format(FlowLangResourceKind.Published, Flow.PublishedVersion);
        Actions = Flow is null ? [] :
        [
            new PageAction(() => FlowLangResources.Get(FlowLangResourceKind.Save), SaveCommand, LucideIconKind.Save),
            new PageAction(() => FlowLangResources.Get(FlowLangResourceKind.Publish), PublishCommand, LucideIconKind.Rocket),
            new PageAction(() => FlowLangResources.Get(FlowLangResourceKind.Run), new RelayCommand(() => StartRequested?.Invoke(this, EventArgs.Empty)), LucideIconKind.Play, IsPrimary: true),
        ];
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (Flow is not { } flow)
        {
            return;
        }
        try
        {
            Flow = await service.SaveDraftAsync(flow, GraphProvider?.Invoke() ?? flow.Draft);
            HasChanges = true;
            RebuildBar();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private async Task PublishAsync()
    {
        if (Flow is not { } flow)
        {
            return;
        }
        try
        {
            if (HasChanges)
            {
                await SaveAsync();
            }
            Flow = await service.PublishAsync((Flow ?? flow).Id);
            HasChanges = false;
            OnPropertyChanged(nameof(VersionText));
            RebuildBar();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    public async Task StartAsync(string? input)
    {
        if (Flow is not { } flow)
        {
            return;
        }
        try
        {
            _poll?.Cancel();
            var run = await service.StartRunAsync(flow.Id, input);
            SetRun(run);
            if (run.State is RunStates.Running or RunStates.Queued)
            {
                _ = PollAsync(run.Id);
            }
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private void Review() => ReviewRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The words follow the language.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(VersionText));
        SetRun(Run);
        RebuildBar();
    }
}
