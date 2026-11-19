namespace TinaClass.Contracts.Common;

/// <summary>
/// What a role may do (design: dotted names, <c>.read</c> to see, <c>.write</c> to add and change, <c>.manage</c> to administer,
/// others' resources included). The API checks one on every endpoint; the app hides what the signed-in user may not do. The roles
/// and their permission sets are in the API (<c>Common/Security/Roles.cs</c>).
/// </summary>
public static class Permissions
{
    public const string ApiKeysManage = "api_keys.manage";
    public const string ChatUse = "chat.use";
    public const string ChatShare = "chat.share";
    public const string ChatHistoryManage = "chat.history.manage";
    public const string ModelsUse = "models.use";
    public const string ModelsManage = "models.manage";
    public const string KeysManage = "keys.manage";
    public const string SettingsManage = "settings.manage";
    public const string KnowledgeRead = "knowledge.read";
    public const string KnowledgeWrite = "knowledge.write";
    public const string KnowledgeManage = "knowledge.manage";
    public const string AgentsUse = "agents.use";
    public const string AgentsWrite = "agents.write";
    public const string AgentsApprove = "agents.approve";
    public const string ToolsManage = "tools.manage";
    public const string WorkflowsWrite = "workflows.write";
    public const string WorkflowsRun = "workflows.run";
    public const string PromptsRead = "prompts.read";
    public const string PromptsWrite = "prompts.write";
    public const string UsageRead = "usage.read";
    public const string UsageManage = "usage.manage";
    public const string TeamMembersManage = "team.members.manage";
    public const string TeamAuditRead = "team.audit.read";

    /// <summary>Every permission, in the order a role editor would list them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ChatUse, ChatShare, ChatHistoryManage, ModelsUse, ModelsManage, KeysManage, SettingsManage, ApiKeysManage,
        KnowledgeRead, KnowledgeWrite, KnowledgeManage,
        AgentsUse, AgentsWrite, AgentsApprove, ToolsManage,
        WorkflowsWrite, WorkflowsRun,
        PromptsRead, PromptsWrite,
        UsageRead, UsageManage,
        TeamMembersManage, TeamAuditRead,
    ];
}
