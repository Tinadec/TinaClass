using Avalonia.Controls;
using TinaClass.App.Features.Chat.Localization;
using TKCode.Arc;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Chat;

/// <summary>The right wing of the chat page (see ChatInspector.axaml). Arc's plan and statistics show their words in the app's language.</summary>
public partial class ChatInspector : UserControl
{
    public ChatInspector()
    {
        InitializeComponent();
        SetLabels();
        ArcLanguage.Changed += (_, _) => SetLabels();
    }

    private void SetLabels()
    {
        static string L(ChatLangResourceKind kind) => ChatLangResources.Get(kind);
        static string F(ChatLangResourceKind kind, params object[] args) => ChatLangResources.Format(kind, args);
        Plan.Labels = new AgentPlanLabels
        {
            Heading = L(ChatLangResourceKind.PlanHeading),
            StepCount = n => F(ChatLangResourceKind.PlanSteps, n),
            Position = (i, n) => F(ChatLangResourceKind.PlanPosition, i, n),
            Approve = L(ChatLangResourceKind.PlanApprove),
            Pause = L(ChatLangResourceKind.PlanPause),
            Resume = L(ChatLangResourceKind.PlanResume),
            StartOver = L(ChatLangResourceKind.PlanStartOver),
            Retry = L(ChatLangResourceKind.PlanRetry),
            Skip = L(ChatLangResourceKind.PlanSkip),
            StateDone = L(ChatLangResourceKind.PlanStateDone),
            StateActive = L(ChatLangResourceKind.PlanStateActive),
            StatePending = L(ChatLangResourceKind.PlanStatePending),
            StateFailed = L(ChatLangResourceKind.PlanStateFailed),
            StateSkipped = L(ChatLangResourceKind.PlanStateSkipped),
            Working = n => F(ChatLangResourceKind.PlanWorking, n),
            Paused = n => F(ChatLangResourceKind.PlanPaused, n),
            Failed = n => F(ChatLangResourceKind.PlanFailed, n),
            DoneIn = d => F(ChatLangResourceKind.PlanDoneIn, d),
            Duration = t => F(ChatLangResourceKind.PlanSecond, Math.Max(1, (int)Math.Round(t.TotalSeconds))),
        };
        Stats.Labels = new GenerationStatsLabels
        {
            FirstToken = L(ChatLangResourceKind.StatsFirstToken),
            Speed = L(ChatLangResourceKind.StatsSpeed),
            Output = L(ChatLangResourceKind.StatsOutput),
            Cost = L(ChatLangResourceKind.StatsCost),
            Done = L(ChatLangResourceKind.StatsDone),
            DoneIn = d => F(ChatLangResourceKind.StatsDoneIn, d),
            Waiting = L(ChatLangResourceKind.StatsWaiting),
            Streaming = L(ChatLangResourceKind.StatsStreaming),
            Stopped = L(ChatLangResourceKind.StatsStopped),
            Failed = L(ChatLangResourceKind.StatsFailed),
            Details = L(ChatLangResourceKind.StatsDetails),
            ShowDetails = L(ChatLangResourceKind.StatsShowDetails),
            HideDetails = L(ChatLangResourceKind.StatsHideDetails),
            Retry = L(ChatLangResourceKind.StatsRetry),
            // The cost is in the app's currency (yuan), not dollars: the unit is a label, the number comes from the service.
            Currency = TinaClass.Contracts.Common.Money.Symbol,
        };
    }
}
