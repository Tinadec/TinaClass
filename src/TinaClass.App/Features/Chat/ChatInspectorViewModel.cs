using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;
using TKCode.Arc;

namespace TinaClass.App.Features.Chat;

/// <summary>One source of the answer, as the inspector lists it.</summary>
public sealed record SourceRow(int Index, string Title, string Where, string Quote, string Score);

/// <summary>
/// What the right wing shows about the answer in view: its plan (Arc's AgentPlan), how it is going (GenerationStats), the sources and
/// the context. <see cref="ChatResponder"/> fills it while an answer streams; opening a conversation fills it from the last answer.
/// </summary>
public sealed partial class ChatInspectorViewModel : ObservableObject
{
    public AvaloniaList<AgentPlanStep> Steps { get; } = [];

    public ObservableCollection<SourceRow> Sources { get; } = [];

    public AvaloniaList<ContextWindowItem> ContextItems { get; } = [];

    public AvaloniaList<ContextWindowModel> ContextModels { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlanStatus))]
    private GenerationStatus _status = GenerationStatus.Idle;

    /// <summary>The plan's own status follows the answer: running until it is done.</summary>
    public AgentPlanStatus PlanStatus => Status is GenerationStatus.Waiting or GenerationStatus.Streaming ? AgentPlanStatus.Running : AgentPlanStatus.Done;

    [ObservableProperty]
    private GenerationUsage _usage = new();

    [ObservableProperty]
    private GenerationModel _model = new("—");

    [ObservableProperty]
    private DateTimeOffset? _startedAt;

    [ObservableProperty]
    private string _costText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    private int _stepCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSources))]
    private int _sourceCount;

    public bool HasPlan => StepCount > 0;

    public bool HasSources => SourceCount > 0;

    public void Reset(string modelName)
    {
        Steps.Clear();
        Sources.Clear();
        StepCount = 0;
        SourceCount = 0;
        Usage = new GenerationUsage();
        CostText = string.Empty;
        Model = new GenerationModel(modelName);
        StartedAt = DateTimeOffset.Now;
        Status = GenerationStatus.Waiting;
    }

    public void SetPlan(IReadOnlyList<PlanStepDto> steps)
    {
        // Same steps as before: only their state changes (the plan's rows animate instead of being rebuilt).
        if (Steps.Count == steps.Count && Steps.Select(s => s.Id).SequenceEqual(steps.Select(s => s.Id)))
        {
            for (var i = 0; i < steps.Count; i++)
            {
                Steps[i].State = StateOf(steps[i].State);
                Steps[i].Duration = steps[i].Ms is { } ms && steps[i].State == "done" ? TimeSpan.FromMilliseconds(ms) : null;
            }
        }
        else
        {
            Steps.Clear();
            foreach (var s in steps)
            {
                Steps.Add(new AgentPlanStep(s.Id, s.Title) { State = StateOf(s.State), Duration = s.Ms is { } ms && s.State == "done" ? TimeSpan.FromMilliseconds(ms) : null });
            }
        }
        StepCount = Steps.Count;
    }

    private static AgentPlanStepState StateOf(string state) => state switch
    {
        "running" => AgentPlanStepState.Active,
        "done" => AgentPlanStepState.Done,
        "error" => AgentPlanStepState.Failed,
        _ => AgentPlanStepState.Pending,
    };

    public void AddSource(int position, string title, string? where, string quote, double? score)
    {
        Sources.Add(new SourceRow(position, title, where ?? string.Empty, quote, score is { } s ? s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : string.Empty));
        SourceCount = Sources.Count;
    }

    public void SetUsage(MessageUsageDto usage, long contextWindow)
    {
        Usage = new GenerationUsage
        {
            InputTokens = usage.TokensIn,
            CachedTokens = usage.TokensCache,
            OutputTokens = usage.TokensOut,
            Ttft = TimeSpan.FromMilliseconds(usage.FirstTokenMs),
            Duration = TimeSpan.FromMilliseconds(usage.TotalMs),
            TokensPerSecond = usage.TotalMs > 0 ? usage.TokensOut * 1000.0 / usage.TotalMs : null,
            // CostUsd is only a number to Arc; the label says yuan (minor units to major).
            CostUsd = usage.Cost / 100.0,
        };
        CostText = (usage.Estimated ? "≈ " : string.Empty) + Money.Format(usage.Cost);
        ContextItems.Clear();
        ContextItems.Add(new ContextWindowItem(ContextWindowCategory.System, "System", 1_200));
        ContextItems.Add(new ContextWindowItem(ContextWindowCategory.Tools, "Tools", Math.Min(usage.TokensIn / 20, 3_000)));
        ContextItems.Add(new ContextWindowItem(ContextWindowCategory.Files, "Files", usage.TokensCache));
        ContextItems.Add(new ContextWindowItem(ContextWindowCategory.Messages, "Messages", Math.Max(0, usage.TokensIn - 1_200)));
        ContextModels.Clear();
        ContextModels.Add(new ContextWindowModel("current", Model.Name, contextWindow, 8_000));
    }
}
