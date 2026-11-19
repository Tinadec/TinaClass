using TinaClass.Contracts.Common;

namespace TinaClass.Api.Common.Security;

/// <summary>
/// The roles and what each may do (design 3). A user has one role (<c>auth_users.RoleId</c>); the API turns it into permissions on
/// every request, so changing a role takes effect at once. The owner may do everything; an administrator everything except
/// <see cref="Permissions.KeysManage"/> (provider keys are the owner's); a member uses and edits; a viewer only uses.
/// </summary>
public static class Roles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";
    public const string Viewer = "viewer";

    private static readonly Dictionary<string, IReadOnlySet<string>> s_permissions = new(StringComparer.Ordinal)
    {
        [Owner] = Permissions.All.ToHashSet(StringComparer.Ordinal),
        [Admin] = Permissions.All.Where(p => p != Permissions.KeysManage).ToHashSet(StringComparer.Ordinal),
        [Member] = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.ApiKeysManage,
            Permissions.ChatUse,
            Permissions.ChatShare,
            Permissions.ModelsUse,
            Permissions.KnowledgeRead,
            Permissions.KnowledgeWrite,
            Permissions.AgentsUse,
            Permissions.AgentsWrite,
            Permissions.WorkflowsRun,
            Permissions.PromptsRead,
            Permissions.PromptsWrite,
            Permissions.UsageRead,
        },
        [Viewer] = new HashSet<string>(StringComparer.Ordinal)
        {
            Permissions.ChatUse,
            Permissions.ModelsUse,
            Permissions.KnowledgeRead,
            Permissions.AgentsUse,
            Permissions.PromptsRead,
        },
    };

    public static IReadOnlyList<string> All { get; } = [Owner, Admin, Member, Viewer];

    public static bool Exists(string roleId) => s_permissions.ContainsKey(roleId);

    /// <summary>The permissions of a role; none for an unknown id.</summary>
    public static IReadOnlySet<string> PermissionsOf(string roleId) =>
        s_permissions.TryGetValue(roleId, out var permissions) ? permissions : new HashSet<string>();
}
