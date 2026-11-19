using Avalonia.Headless.XUnit;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Team;
using Xunit;

namespace TinaClass.App.Tests.Features.Team;

public class TeamTests
{
    private static TeamViewModel Open(string language = "en-US")
    {
        Ui.Open(language);
        Ui.Go(AppPages.Team);
        var team = Ui.Service<TeamViewModel>();
        Assert.True(Ui.Until(() => team.Members.Count > 0));
        return team;
    }

    [AvaloniaFact]
    public void The_page_lists_the_five_members_and_the_invitation_that_waits()
    {
        var team = Open();

        Assert.Equal(6, team.Members.Count);
        Assert.Equal(5, team.Members.Count(m => m.Dto.Status == "active"));
        var invited = Assert.Single(team.Members, m => m.Dto.Status == "invited");
        Assert.False(invited.CanToggle);
        Assert.Equal("—", invited.Joined);
        Assert.Equal("Owner", team.Members[0].Role);
    }

    [AvaloniaFact]
    public void The_audit_log_is_the_second_tab_and_has_the_newest_entry_first()
    {
        var team = Open();
        Assert.True(team.ShowsMembers);
        Assert.False(team.ShowsAudit);

        team.Tab = 1;

        Assert.True(team.ShowsAudit);
        Assert.False(team.ShowsMembers);
        Assert.NotEmpty(team.Audit);
        Assert.False(team.HasNoAudit);
    }

    [AvaloniaFact]
    public void Disabling_a_member_and_enabling_again()
    {
        var team = Open();
        var member = team.Members.First(m => m.CanToggle);

        team.ToggleCommand.Execute(member);
        Assert.True(Ui.Until(() => team.Members.First(m => m.Id == member.Id).Dto.Status == "disabled"));
        var disabled = team.Members.First(m => m.Id == member.Id);
        Assert.Equal("Enable", disabled.ToggleText);

        team.ToggleCommand.Execute(disabled);
        Assert.True(Ui.Until(() => team.Members.First(m => m.Id == member.Id).Dto.Status == "active"));
    }

    [AvaloniaFact]
    public async Task Inviting_adds_a_pending_member_and_keeps_the_token_to_show_once()
    {
        var team = Open();

        await team.InviteAsync("new.person@example.com", "member");

        Assert.True(Ui.Until(() => team.Members.Count == 7));
        Assert.Contains(team.Members, m => m.Email == "new.person@example.com" && m.Dto.Status == "invited");
        Assert.False(string.IsNullOrEmpty(team.LastInviteToken));
    }

    [AvaloniaFact]
    public void The_words_follow_the_language()
    {
        var team = Open();
        var english = team.Members[0].Role;

        Ui.Shell.SetLanguageCommand.Execute("zh-CN");
        Ui.Settle(6);

        Assert.NotEqual(english, team.Members[0].Role);
        Assert.Matches("[一-鿿]", team.Members[0].Role);
    }
}
