namespace TinaClass.Contracts.Workflows;

public static class NodeKinds
{
    public const string Trigger = "trigger";
    public const string Model = "model";
    public const string Retrieve = "retrieve";
    public const string Branch = "branch";
    public const string Tool = "tool";
    public const string Approval = "approval";
    public const string Send = "send";
}

public static class NodeStates
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
    public const string Waiting = "waiting";
}

public sealed record FlowNodeDto(string Key, string Kind, string Title, string Subtitle, int X, int Y);

public sealed record FlowEdgeDto(string From, string To, string? Label);

public sealed record FlowGraphDto(IReadOnlyList<FlowNodeDto> Nodes, IReadOnlyList<FlowEdgeDto> Edges);

public sealed record FlowDto(Guid Id, string Name, int PublishedVersion, FlowGraphDto Draft, bool HasChanges, DateTimeOffset UpdatedAt, int Version);

public sealed record CreateFlowRequest(string Name);

public sealed record PutDraftRequest(FlowGraphDto Graph, int Version);

public sealed record FlowVersionDto(Guid Id, int Number, string PublishedByName, DateTimeOffset PublishedAt);

public sealed record FlowNodeRunDto(string Key, string State, int StartMs, int DurationMs, string? Output);

public sealed record FlowRunDto(Guid Id, Guid FlowId, int FlowVersion, string State, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, long Cost, int Ordinal,
    IReadOnlyList<FlowNodeRunDto> Nodes);

public sealed record StartFlowRunRequest(string? Input);

public static class WorkflowsEvents
{
    public const string Published = "flow.published";
    public const string RunCompleted = "flow.run_completed";
}
