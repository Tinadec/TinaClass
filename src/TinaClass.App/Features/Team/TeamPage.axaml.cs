using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using TinaClass.App.Features.Team.Localization;
using TKCode.Arc;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Team;

/// <summary>The team page: a <c>Table</c> of the members (a <c>Badge</c> for the status, a button to disable or enable) and one for the audit log, and the invitation dialog.</summary>
public partial class TeamPage : UserControl
{
    private TeamViewModel? _viewModel;

    public TeamPage()
    {
        InitializeComponent();
        ArcLanguage.Changed += (_, _) =>
        {
            Fill();
            _viewModel?.RefreshTexts();
        };
        Fill();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TeamViewModel vm && vm != _viewModel)
        {
            _viewModel = vm;
            vm.InviteRequested += async (_, _) => await InviteAsync(vm);
        }
    }

    private void Fill()
    {
        string T(TeamLangResourceKind kind) => TeamLangResources.Get(kind);
        Tabs.Items.Clear();
        Tabs.Items.Add(new SegmentedOption(0, T(TeamLangResourceKind.TabMembers)));
        Tabs.Items.Add(new SegmentedOption(1, T(TeamLangResourceKind.TabAudit)));
        MemberTable.Columns.Clear();
        MemberTable.Columns.Add(new DataTableColumn(T(TeamLangResourceKind.ColName), o => ((MemberRow)o).Name) { Width = new GridLength(1.2, GridUnitType.Star) });
        MemberTable.Columns.Add(new DataTableColumn(T(TeamLangResourceKind.ColEmail), o => ((MemberRow)o).Email) { Width = new GridLength(2, GridUnitType.Star) });
        MemberTable.Columns.Add(new DataTableColumn(T(TeamLangResourceKind.ColRole), o => ((MemberRow)o).Role) { Width = new GridLength(1, GridUnitType.Star) });
        MemberTable.Columns.Add(new DataTableColumn { Header = T(TeamLangResourceKind.ColStatus), Width = new GridLength(110), CellTemplate = new FuncDataTemplate<MemberRow>((row, _) => new Badge { Tone = row.StatusTone, Content = row.Status, HorizontalAlignment = HorizontalAlignment.Left }) });
        MemberTable.Columns.Add(new DataTableColumn(T(TeamLangResourceKind.ColJoined), o => ((MemberRow)o).Joined) { Width = new GridLength(96) });
        MemberTable.Columns.Add(new DataTableColumn
        {
            Header = string.Empty,
            Width = new GridLength(96),
            CellTemplate = new FuncDataTemplate<MemberRow>((row, _) =>
            {
                var button = new TKCode.Arc.Button { Content = row.ToggleText, Variant = ButtonVariant.Ghost, IsVisible = row.CanToggle, HorizontalAlignment = HorizontalAlignment.Right };
                button.Classes.Add("compact");
                button.Click += (_, _) => _viewModel?.ToggleCommand.Execute(row);
                return button;
            }),
        });
        AuditTable.Columns.Clear();
        AuditTable.Columns.Add(new DataTableColumn(T(TeamLangResourceKind.ColWhen), o => ((AuditRow)o).When) { Width = new GridLength(110) });
        AuditTable.Columns.Add(new DataTableColumn(T(TeamLangResourceKind.ColActor), o => ((AuditRow)o).Actor) { Width = new GridLength(1, GridUnitType.Star) });
        AuditTable.Columns.Add(new DataTableColumn(T(TeamLangResourceKind.ColAction), o => ((AuditRow)o).Action) { Width = new GridLength(1.4, GridUnitType.Star) });
        AuditTable.Columns.Add(new DataTableColumn(T(TeamLangResourceKind.ColDetail), o => ((AuditRow)o).Detail) { Width = new GridLength(2.6, GridUnitType.Star) });
    }

    private async Task InviteAsync(TeamViewModel vm)
    {
        string T(TeamLangResourceKind kind) => TeamLangResources.Get(kind);
        var email = new Input { Label = T(TeamLangResourceKind.InviteEmail), Placeholder = "name@company.example" };
        var role = new Select { Label = T(TeamLangResourceKind.InviteRole) };
        role.Items.Add(new SelectOption("admin", T(TeamLangResourceKind.RoleAdmin)));
        role.Items.Add(new SelectOption("member", T(TeamLangResourceKind.RoleMember)));
        role.Items.Add(new SelectOption("viewer", T(TeamLangResourceKind.RoleViewer)));
        role.SelectedIndex = 1;
        var result = await Dialog.ShowAsync(this, T(TeamLangResourceKind.InviteTitle), new StackPanel { MinWidth = 380, Spacing = 12, Children = { email, role } }, null,
            new DialogAction(T(TeamLangResourceKind.Cancel), false, ButtonVariant.Secondary),
            new DialogAction(T(TeamLangResourceKind.InviteSend), true, ButtonVariant.Primary));
        if (result is true && !string.IsNullOrWhiteSpace(email.Text))
        {
            var chosen = role.SelectedItem is SelectOption option ? option.Value as string : null;
            await vm.InviteAsync(email.Text.Trim(), chosen ?? "member");
        }
    }
}
