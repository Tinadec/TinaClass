namespace TinaClass.Contracts.Agents;

public static class ToolRisks
{
    public const string Read = "read";
    public const string Write = "write";
    public const string External = "external";
}

public static class ToolApprovals
{
    public const string Auto = "auto";
    public const string Ask = "ask";
    public const string Always = "always";
}

/// <summary>The run state machine (design 4.3).</summary>
public static class RunStates
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string WaitingApproval = "waiting_approval";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsFinal(string s) => s is Succeeded or Failed or Cancelled;
}

public sealed record AgentToolRef(Guid ToolId, string Name, string Risk, bool Enabled, string Approval);

public sealed record AgentDto(Guid Id, string Name, string Description, string Instruction, string ModelCode, int MaxSteps, long MaxCost, string Visibility, string State,
    string? Schedule, string Icon, IReadOnlyList<AgentToolRef> Tools, IReadOnlyList<Guid> BaseIds, int Runs, int SuccessPercent, string? LiveState, int Version);

public sealed record SaveAgentRequest(string Name, string Description, string Instruction, string ModelCode, int MaxSteps, long MaxCost, string Visibility, string? Schedule,
    string? Icon, IReadOnlyList<AgentToolSetting>? Tools, IReadOnlyList<Guid>? BaseIds, int Version);

public sealed record AgentToolSetting(Guid ToolId, bool Enabled, string Approval);

public sealed record ToolServerDto(Guid Id, string Name, string Kind, string Endpoint, string Status, int ToolCount, DateTimeOffset? LastSeenAt);

public sealed record CreateToolServerRequest(string Name, string Kind, string Endpoint);

public sealed record ToolDto(Guid Id, Guid ServerId, string ServerName, string Name, string Description, string Risk);

public sealed record UpdateToolRequest(string? Risk);

public sealed record RunStepDto(Guid Id, int Ordinal, string Kind, string Title, string Detail, string State, int AtMs);

public sealed record RunDto(Guid Id, Guid AgentId, string AgentName, string Trigger, string Subject, string State, DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
    int Tokens, long Cost, IReadOnlyList<RunStepDto> Steps, int Version);

public sealed record StartRunRequest(string Subject);

public sealed record ApprovalDto(Guid Id, string Source, Guid? RunId, string Title, string Detail, IReadOnlyList<KeyValuePair<string, string>> Arguments, string Risk, string State,
    string? Reason, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt);

public sealed record DecideApprovalRequest(string? Reason);

public static class AgentsEvents
{
    public const string RunWaitingApproval = "run.waiting_approval";
    public const string RunCompleted = "run.completed";
}

public static class AgentsAuditActions
{
    public const string ApprovalApproved = "agents.approval_approved";
    public const string ApprovalRejected = "agents.approval_rejected";
    public const string ToolChanged = "agents.tool_changed";
}
