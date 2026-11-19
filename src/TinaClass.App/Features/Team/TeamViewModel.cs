using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Ui;
using TinaClass.App.Features.Team.Localization;
using TinaClass.Contracts.Team;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Features.Team;

/// <summary>A member row: name, email, role and status as words, and whether the person can be disabled or enabled.</summary>
public sealed class MemberRow(MemberDto dto) : ObservableObject
{
    public MemberDto Dto { get; } = dto;

    public Guid Id => Dto.Id;

    public string Name => Dto.Name;

    public string Email => Dto.Email;

    public string Role => TeamViewModel.RoleText(Dto.RoleId);

    public string Status => TeamLangResources.Get(Dto.Status switch
    {
        "invited" => TeamLangResourceKind.StatusInvited,
        "disabled" => TeamLangResourceKind.StatusDisabled,
        _ => TeamLangResourceKind.StatusActive,
    });

    public BadgeTone StatusTone => Dto.Status switch { "invited" => BadgeTone.Warning, "disabled" => BadgeTone.Neutral, _ => BadgeTone.Success };

    public string Joined => Dto.JoinedAt is { } at ? TimeText.Short(at) : "—";

    public bool CanToggle => Dto.Status != "invited";

    public string ToggleText => TeamLangResources.Get(Dto.Status == "disabled" ? TeamLangResourceKind.Enable : TeamLangResourceKind.Disable);
}

/// <summary>An audit row.</summary>
public sealed record AuditRow(string Actor, string Action, string Detail, string When);

/// <summary>The team page: the members (disable, enable, invite) and the audit log. The server decides what is allowed (the last owner stays an owner).</summary>
public sealed partial class TeamViewModel(ITeamService service) : PageViewModel
{
    public ObservableCollection<MemberRow> Members { get; } = [];

    public ObservableCollection<AuditRow> Audit { get; } = [];

    /// <summary>0 members, 1 audit log.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMembers), nameof(ShowsAudit))]
    private int _tab;

    public bool ShowsMembers => Tab == 0;

    public bool ShowsAudit => Tab == 1;

    public bool HasNoAudit => Audit.Count == 0;

    /// <summary>"Invite a member": the page asks for the email and role.</summary>
    public event EventHandler? InviteRequested;

    public ITeamService Service => service;

    protected override async Task LoadAsync()
    {
        Actions = [new PageAction(() => TeamLangResources.Get(TeamLangResourceKind.Invite), new RelayCommand(() => InviteRequested?.Invoke(this, EventArgs.Empty)), LucideIconKind.UserPlus, IsPrimary: true)];
        try
        {
            Members.Clear();
            foreach (var m in await service.MembersAsync())
            {
                Members.Add(new MemberRow(m));
            }
            Audit.Clear();
            foreach (var a in await service.AuditAsync())
            {
                Audit.Add(new AuditRow(a.ActorName, a.Action, a.Detail, a.At.LocalDateTime.ToString("M/d HH:mm", Common.Localization.Text.Culture)));
            }
            OnPropertyChanged(nameof(HasNoAudit));
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    public async Task InviteAsync(string email, string roleId)
    {
        try
        {
            var invite = await service.InviteAsync(email, roleId);
            LastInviteToken = invite.Token;
            await RefreshAsync();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>The token of the invitation just made (shown once, then forgotten).</summary>
    public string? LastInviteToken { get; private set; }

    [RelayCommand]
    private async Task ToggleAsync(MemberRow row)
    {
        try
        {
            await service.UpdateAsync(row.Id, null, row.Dto.Status != "disabled");
            await RefreshAsync();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    internal static string RoleText(string roleId) => TeamLangResources.Get(roleId switch
    {
        "owner" => TeamLangResourceKind.RoleOwner,
        "admin" => TeamLangResourceKind.RoleAdmin,
        "viewer" => TeamLangResourceKind.RoleViewer,
        _ => TeamLangResourceKind.RoleMember,
    });

    /// <summary>The words follow the language.</summary>
    public void RefreshTexts()
    {
        _ = RefreshAsync();
        Actions = [new PageAction(() => TeamLangResources.Get(TeamLangResourceKind.Invite), new RelayCommand(() => InviteRequested?.Invoke(this, EventArgs.Empty)), LucideIconKind.UserPlus, IsPrimary: true)];
    }
}
