using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Ui;
using TinaClass.App.Features.Agents.Localization;
using TinaClass.Contracts.Agents;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Features.Agents;

/// <summary>An agent in the list: name, what it does, how it has done, and whether a run is live.</summary>
public sealed class AgentRow(AgentDto dto) : ObservableObject
{
    public AgentDto Dto { get; } = dto;

    public Guid Id => Dto.Id;

    public string Name => Dto.Name;

    public string Description => Dto.Description;

    public LucideIconKind Icon => IconKind(Dto.Icon);

    public string Meta => AgentsLangResources.Format(AgentsLangResourceKind.Success, Dto.SuccessPercent) + " · " + AgentsLangResources.Format(AgentsLangResourceKind.Runs, Dto.Runs);

    public bool HasLive => Dto.LiveState is not null;

    public string LiveText => Dto.LiveState is null ? string.Empty : AgentsViewModel.StateText(Dto.LiveState);

    public BadgeTone LiveTone => AgentsViewModel.StateTone(Dto.LiveState ?? string.Empty);

    public void Refresh() => OnPropertyChanged(string.Empty);

    private static LucideIconKind IconKind(string name) => name switch
    {
        "bulb" => LucideIconKind.Lightbulb,
        "ticket" => LucideIconKind.Ticket,
        "calendar" => LucideIconKind.CalendarDays,
        "database" => LucideIconKind.Database,
        "globe" => LucideIconKind.Globe,
        "mic" => LucideIconKind.Mic,
        _ => LucideIconKind.Bot,
    };
}

/// <summary>A tool of the chosen agent with its risk and how it is approved.</summary>
public sealed record ToolRow(string Name, string Risk, BadgeTone RiskTone, string Approval);

/// <summary>A tool server and whether it answers.</summary>
public sealed record ServerRow(string Name, string Endpoint, StatusTone Tone, string Status, string Tools, string Seen);

/// <summary>An earlier run of the chosen agent.</summary>
public sealed record PastRunRow(RunDto Run, string Subject, string State, BadgeTone Tone, string When, string Cost);

/// <summary>
/// The agents page: the agents, the chosen one's tools, its latest run as an <c>AgentPlan</c> (the page builds the plan from <see cref="Run"/> whenever
/// <see cref="RunChanged"/> is raised), the earlier runs and the tool servers. A live run is read again every second until it ends or waits for approval.
/// An approval that waits is shown in a banner and reviewed in the dialog (<see cref="ReviewRequested"/>).
/// </summary>
public sealed partial class AgentsViewModel(IAgentsService service) : PageViewModel
{
    public ObservableCollection<AgentRow> Items { get; } = [];

    public ObservableCollection<ToolRow> Tools { get; } = [];

    public ObservableCollection<ServerRow> Servers { get; } = [];

    public ObservableCollection<PastRunRow> PastRuns { get; } = [];

    [ObservableProperty]
    private AgentRow? _selected;

    [ObservableProperty]
    private RunDto? _run;

    [ObservableProperty]
    private string _runSubject = string.Empty;

    [ObservableProperty]
    private string _runState = string.Empty;

    [ObservableProperty]
    private BadgeTone _runTone;

    [ObservableProperty]
    private string _runMeta = string.Empty;

    [ObservableProperty]
    private string _pendingText = string.Empty;

    [ObservableProperty]
    private bool _hasPending;

    public bool HasRun => Run is not null;

    public bool HasNoRun => Run is null && Selected is not null;

    public bool HasNoAgents => Items.Count == 0 && !IsLoading;

    public bool HasAgent => Selected is not null;

    public string SelectedName => Selected?.Name ?? "";

    public string SelectedDescription => Selected?.Description ?? "";

    public bool HasPastRuns => PastRuns.Count > 0;

    public IReadOnlyList<KeyValuePair<string, string>> Facts { get; private set; } = [];

    private Guid? _pendingId;
    private CancellationTokenSource? _poll;

    /// <summary>The run to show changed (another agent, a new step, a decision); the page fills the plan.</summary>
    public event EventHandler? RunChanged;

    /// <summary>The banner's "Review": the page opens the approval dialog.</summary>
    public event EventHandler<Guid?>? ReviewRequested;

    /// <summary>"Run now": the page asks what to work on, then calls <see cref="StartAsync"/>.</summary>
    public event EventHandler? StartRequested;

    public IAgentsService Service => service;

    protected override async Task LoadAsync()
    {
        service.ApprovalsChanged -= OnApprovalsChanged;
        service.ApprovalsChanged += OnApprovalsChanged;
        try
        {
            await ReloadAsync(Selected?.Id);
            Servers.Clear();
            foreach (var s in await service.ServersAsync())
            {
                Servers.Add(ServerOf(s));
            }
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    private async void OnApprovalsChanged(object? sender, EventArgs e)
    {
        try
        {
            await ReloadAsync(Selected?.Id);
        }
        catch (ServiceException ex)
        {
            Error = ex.Message;
        }
    }

    private async Task ReloadAsync(Guid? select)
    {
        var agents = await service.AgentsAsync();
        Items.Clear();
        foreach (var a in agents)
        {
            Items.Add(new AgentRow(a));
        }
        OnPropertyChanged(nameof(HasNoAgents));
        await RefreshPendingAsync();
        Selected = Items.FirstOrDefault(a => a.Id == select) ?? Items.FirstOrDefault(a => a.HasLive) ?? Items.FirstOrDefault();
    }

    private async Task RefreshPendingAsync()
    {
        var pending = await service.PendingApprovalsAsync();
        _pendingId = pending.FirstOrDefault()?.Id;
        HasPending = pending.Count > 0;
        PendingText = AgentsLangResources.Format(AgentsLangResourceKind.Pending, pending.Count);
    }

    partial void OnSelectedChanged(AgentRow? value)
    {
        OnPropertyChanged(nameof(HasAgent));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedDescription));
        _ = ShowAgentAsync(value);
    }

    private async Task ShowAgentAsync(AgentRow? agent)
    {
        _poll?.Cancel();
        Tools.Clear();
        PastRuns.Clear();
        SetRun(null);
        if (agent is null)
        {
            RebuildBar();
            return;
        }
        var dto = agent.Dto;
        foreach (var t in dto.Tools)
        {
            Tools.Add(new ToolRow(t.Name, ApprovalReview.RiskText(t.Risk), t.Risk switch { ToolRisks.External => BadgeTone.Warning, ToolRisks.Write => BadgeTone.Info, _ => BadgeTone.Neutral },
                AgentsLangResources.Get(t.Approval switch { ToolApprovals.Always => AgentsLangResourceKind.ApprovalAlways, ToolApprovals.Ask => AgentsLangResourceKind.ApprovalAsk, _ => AgentsLangResourceKind.ApprovalAuto })));
        }
        var schedule = dto.Schedule ?? AgentsLangResources.Get(AgentsLangResourceKind.Manual);
        Facts =
        [
            new(AgentsLangResources.Get(AgentsLangResourceKind.Model), dto.ModelCode),
            new(AgentsLangResources.Get(AgentsLangResourceKind.Schedule), schedule),
            new(AgentsLangResources.Get(AgentsLangResourceKind.Bases), dto.BaseIds.Count == 0 ? "—" : AgentsLangResources.Format(AgentsLangResourceKind.BasesCount, dto.BaseIds.Count)),
            new(AgentsLangResources.Get(AgentsLangResourceKind.Instruction), dto.Instruction),
        ];
        OnPropertyChanged(nameof(Facts));
        try
        {
            var runs = await service.RunsAsync(dto.Id);
            foreach (var past in runs.Skip(1))
            {
                PastRuns.Add(PastOf(past));
            }
            OnPropertyChanged(nameof(HasPastRuns));
            SetRun(runs.FirstOrDefault());
            RebuildBar();
            if (Run is { } run && !RunStates.IsFinal(run.State) && run.State != RunStates.WaitingApproval)
            {
                _ = PollAsync(run.Id);
            }
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    private void SetRun(RunDto? run)
    {
        Run = run;
        RunSubject = run?.Subject ?? string.Empty;
        RunState = run is null ? string.Empty : StateText(run.State);
        RunTone = run is null ? BadgeTone.Neutral : StateTone(run.State);
        RunMeta = run is null ? string.Empty : AgentsLangResources.Format(AgentsLangResourceKind.RunMeta, run.Tokens.ToString("N0", CultureInfo.InvariantCulture), Money(run.Cost));
        OnPropertyChanged(nameof(HasRun));
        OnPropertyChanged(nameof(HasNoRun));
        RunChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>While the run is going, reads it every second and shows the new steps.</summary>
    private async Task PollAsync(Guid runId)
    {
        var source = _poll = new CancellationTokenSource();
        try
        {
            while (!source.IsCancellationRequested)
            {
                await Task.Delay(1000, source.Token);
                var run = await service.RunAsync(runId, source.Token);
                if (source.IsCancellationRequested)
                {
                    return;
                }
                SetRun(run);
                if (RunStates.IsFinal(run.State) || run.State == RunStates.WaitingApproval)
                {
                    await RefreshRowAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Another agent was chosen; its own poll takes over.
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Reads the agent again (its success rate and live state changed) and puts the new row in the list.</summary>
    private async Task RefreshRowAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }
        var fresh = (await service.AgentsAsync()).FirstOrDefault(a => a.Id == row.Id);
        var index = Items.IndexOf(row);
        if (fresh is not null && index >= 0)
        {
            var replaced = new AgentRow(fresh);
            _poll?.Cancel();
            Items[index] = replaced;
            Selected = replaced;
        }
    }

    private void RebuildBar()
    {
        Crumbs = Selected is { } s ? [s.Name] : [];
        BarNote = null;
        var actions = new List<PageAction>();
        if (HasPending)
        {
            actions.Add(new PageAction(() => AgentsLangResources.Get(AgentsLangResourceKind.Review), ReviewCommand, LucideIconKind.ShieldCheck));
        }
        if (Selected is not null)
        {
            actions.Add(new PageAction(() => AgentsLangResources.Get(AgentsLangResourceKind.RunNow), new RelayCommand(() => StartRequested?.Invoke(this, EventArgs.Empty)), LucideIconKind.Play, IsPrimary: true));
        }
        Actions = actions;
    }

    partial void OnHasPendingChanged(bool value) => RebuildBar();

    [RelayCommand]
    private void Review() => ReviewRequested?.Invoke(this, _pendingId);

    public async Task StartAsync(string subject)
    {
        if (Selected is not { } row)
        {
            return;
        }
        try
        {
            _poll?.Cancel();
            var run = await service.StartRunAsync(row.Id, subject);
            SetRun(run);
            if (RunStates.IsFinal(run.State))
            {
                await RefreshRowAsync();
            }
            else
            {
                _ = PollAsync(run.Id);
            }
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>The words follow the language.</summary>
    public void RefreshTexts()
    {
        foreach (var row in Items)
        {
            row.Refresh();
        }
        _ = RefreshPendingAsync();
        if (Selected is not null)
        {
            _ = ShowAgentAsync(Selected);
        }
        RebuildBar();
    }

    private static PastRunRow PastOf(RunDto run) => new(run, run.Subject, StateText(run.State), StateTone(run.State), TimeText.Short(run.StartedAt), Money(run.Cost));

    private static ServerRow ServerOf(ToolServerDto s) => new(s.Name, s.Endpoint, s.Status == "ok" ? StatusTone.Success : StatusTone.Danger,
        AgentsLangResources.Get(s.Kind == "builtin" ? AgentsLangResourceKind.ServerBuiltin : s.Status == "ok" ? AgentsLangResourceKind.ServerOk : AgentsLangResourceKind.ServerError),
        AgentsLangResources.Format(AgentsLangResourceKind.ServerTools, s.ToolCount), s.LastSeenAt is { } seen ? AgentsLangResources.Format(AgentsLangResourceKind.ServerSeen, TimeText.Short(seen)) : string.Empty);

    internal static string Money(long minor) => TinaClass.Contracts.Common.Money.Format(minor);

    internal static string StateText(string state) => AgentsLangResources.Get(state switch
    {
        RunStates.Queued => AgentsLangResourceKind.StateQueued,
        RunStates.Running => AgentsLangResourceKind.StateRunning,
        RunStates.WaitingApproval => AgentsLangResourceKind.StateWaiting,
        RunStates.Succeeded => AgentsLangResourceKind.StateSucceeded,
        RunStates.Failed => AgentsLangResourceKind.StateFailed,
        _ => AgentsLangResourceKind.StateCancelled,
    });

    internal static BadgeTone StateTone(string state) => state switch
    {
        RunStates.Queued or RunStates.WaitingApproval => BadgeTone.Warning,
        RunStates.Running => BadgeTone.Info,
        RunStates.Succeeded => BadgeTone.Success,
        RunStates.Failed => BadgeTone.Danger,
        _ => BadgeTone.Neutral,
    };
}
