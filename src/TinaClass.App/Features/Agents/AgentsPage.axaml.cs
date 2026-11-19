using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.App.Common.Services;
using TinaClass.App.Features.Agents.Localization;
using TinaClass.Contracts.Agents;
using TKCode.Arc;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Agents;

/// <summary>
/// The agents page. Arc's <c>AgentPlan</c> shows the run: one step per <see cref="RunStepDto"/>, in the state the run reports (a run that waits for an
/// approval is a paused plan whose "Resume" button reads "Review approval" and opens the dialog). The plan's words come from the app's language.
/// </summary>
public partial class AgentsPage : UserControl
{
    private AgentsViewModel? _viewModel;

    public AgentsPage()
    {
        InitializeComponent();
        Plan.Labels = Labels();
        Plan.PauseChanged += async (_, e) =>
        {
            // "Resume" of a plan that waits for an approval means: look at the approval.
            if (!e.IsPaused && _viewModel?.Run?.State == RunStates.WaitingApproval)
            {
                await ReviewAsync(null);
            }
        };
        ArcLanguage.Changed += (_, _) =>
        {
            Plan.Labels = Labels();
            _viewModel?.RefreshTexts();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is AgentsViewModel vm && vm != _viewModel)
        {
            _viewModel = vm;
            vm.RunChanged += (_, _) => FillPlan(vm.Run);
            vm.ReviewRequested += async (_, id) => await ReviewAsync(id);
            vm.StartRequested += async (_, _) => await StartAsync(vm);
        }
    }

    private async Task ReviewAsync(Guid? id)
    {
        await DesktopApp.Services.GetRequiredService<IApprovalReview>().ReviewAsync(this, id);
    }

    private async Task StartAsync(AgentsViewModel vm)
    {
        if (vm.Selected is not { } agent)
        {
            return;
        }
        var subject = new Input { Label = AgentsLangResources.Get(AgentsLangResourceKind.RunSubject), Text = AgentsLangResources.Get(AgentsLangResourceKind.RunSubjectDefault) };
        var result = await Dialog.ShowAsync(this, AgentsLangResources.Format(AgentsLangResourceKind.RunNowTitle, agent.Name), new StackPanel { MinWidth = 380, Children = { subject } }, null,
            new DialogAction(AgentsLangResources.Get(AgentsLangResourceKind.Cancel), false, ButtonVariant.Secondary),
            new DialogAction(AgentsLangResources.Get(AgentsLangResourceKind.Start), true, ButtonVariant.Primary));
        if (result is true && !string.IsNullOrWhiteSpace(subject.Text))
        {
            await vm.StartAsync(subject.Text.Trim());
        }
    }

    /// <summary>Builds the plan from the run: a step per step of the run (kept when it is already there, so a new step appears and the others stay still).</summary>
    private void FillPlan(RunDto? run)
    {
        if (run is null)
        {
            Plan.Steps.Clear();
            return;
        }
        Plan.Goal = run.Subject;
        var existing = Plan.Steps.ToDictionary(s => s.Id);
        if (Plan.Steps.Count > run.Steps.Count || Plan.Steps.Where((s, i) => run.Steps[i].Id.ToString() != s.Id).Any())
        {
            Plan.Steps.Clear();
            existing.Clear();
        }
        for (var i = 0; i < run.Steps.Count; i++)
        {
            var source = run.Steps[i];
            var state = source.State switch
            {
                "done" => AgentPlanStepState.Done,
                "running" or "waiting" => AgentPlanStepState.Active,
                "failed" => AgentPlanStepState.Failed,
                "skipped" => AgentPlanStepState.Skipped,
                _ => AgentPlanStepState.Pending,
            };
            var next = i + 1 < run.Steps.Count ? run.Steps[i + 1].AtMs : source.AtMs;
            if (!existing.TryGetValue(source.Id.ToString(), out var step))
            {
                step = new AgentPlanStep(source.Id.ToString(), source.Title);
                if (!string.IsNullOrWhiteSpace(source.Detail))
                {
                    step.Activities.Add(source.Detail);
                }
                Plan.Steps.Add(step);
            }
            step.State = state;
            if (state == AgentPlanStepState.Failed)
            {
                step.Error = source.Detail;
            }
            step.Duration = state == AgentPlanStepState.Done && next > source.AtMs ? TimeSpan.FromMilliseconds(next - source.AtMs) : null;
        }
        Plan.Status = run.State switch
        {
            RunStates.WaitingApproval => AgentPlanStatus.Paused,
            RunStates.Queued or RunStates.Running => AgentPlanStatus.Running,
            _ => AgentPlanStatus.Done,
        };
    }

    /// <summary>AgentPlan's words in the app's language (the rest keep Arc's English).</summary>
    private static AgentPlanLabels Labels()
    {
        string L(AgentsLangResourceKind kind) => AgentsLangResources.Get(kind);
        string F(AgentsLangResourceKind kind, params object[] args) => AgentsLangResources.Format(kind, args);
        return new AgentPlanLabels
        {
            Heading = L(AgentsLangResourceKind.PHeading),
            StepCount = n => F(AgentsLangResourceKind.PStepCount, n),
            Position = (i, n) => F(AgentsLangResourceKind.PPosition, i, n),
            Approve = L(AgentsLangResourceKind.PApprove),
            Pause = L(AgentsLangResourceKind.PPause),
            Resume = L(AgentsLangResourceKind.PResume),
            StartOver = L(AgentsLangResourceKind.PStartOver),
            Retry = L(AgentsLangResourceKind.PRetry),
            Skip = L(AgentsLangResourceKind.PSkip),
            StateDone = L(AgentsLangResourceKind.PStateDone),
            StateActive = L(AgentsLangResourceKind.PStateActive),
            StatePending = L(AgentsLangResourceKind.PStatePending),
            StateFailed = L(AgentsLangResourceKind.PStateFailed),
            StateSkipped = L(AgentsLangResourceKind.PStateSkipped),
            Working = n => F(AgentsLangResourceKind.PWorking, n),
            Paused = n => F(AgentsLangResourceKind.PPaused, n),
            Failed = n => F(AgentsLangResourceKind.PFailed, n),
            DoneIn = d => F(AgentsLangResourceKind.PDoneIn, d),
            Duration = t => F(AgentsLangResourceKind.PSecond, Math.Max(1, (int)Math.Round(t.TotalSeconds))),
        };
    }
}
