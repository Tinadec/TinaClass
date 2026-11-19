using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Team.Localization;

/// <summary>The text of the Team feature (one member per string; the .xlf files next to this one hold en-US, zh-CN and zh-TW).</summary>
[LanguageCatalog]
public enum TeamLangResourceKind
{
    NavTeam,
    Title,
    Lead,
    TabMembers,
    TabAudit,
    Invite,
    InviteTitle,
    InviteEmail,
    InviteRole,
    InviteSend,
    InviteSent,
    InviteLink,
    Close,
    Cancel,
    ColName,
    ColEmail,
    ColRole,
    ColStatus,
    ColJoined,
    ColActor,
    ColAction,
    ColDetail,
    ColWhen,
    StatusActive,
    StatusInvited,
    StatusDisabled,
    RoleOwner,
    RoleAdmin,
    RoleMember,
    RoleViewer,
    Disable,
    Enable,
    EmptyAudit,
    LoadError,
    FilterAll,
    ErrorLastOwner,
}
