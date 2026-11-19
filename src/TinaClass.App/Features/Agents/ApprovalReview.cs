using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Services;
using TinaClass.App.Common.Ui;
using TinaClass.App.Features.Agents.Localization;
using TinaClass.Contracts.Agents;
using TKCode.Arc;

namespace TinaClass.App.Features.Agents;

/// <summary>
/// The approval dialog: what the agent wants to do (a hand-rolled <see cref="DescriptionList"/> of the arguments), its risk, a reason for a rejection, and
/// Arc's <c>HoldToConfirm</c> to approve (the ring fills while the button is held and falls back when it is let go). Rejecting and cancelling are plain
/// buttons. Esc cancels.
/// </summary>
public sealed class ApprovalReview(IAgentsService service) : IApprovalReview
{
    public async Task<bool> ReviewAsync(Visual owner, Guid? approvalId = null)
    {
        ApprovalDto? approval;
        try
        {
            var pending = await service.PendingApprovalsAsync();
            approval = approvalId is { } id ? pending.FirstOrDefault(a => a.Id == id) : pending.FirstOrDefault();
        }
        catch (ServiceException)
        {
            return false;
        }
        if (approval is null)
        {
            return false;
        }

        var reason = new Input { Label = AgentsLangResources.Get(AgentsLangResourceKind.RejectReason), Text = AgentsLangResources.Get(AgentsLangResourceKind.RejectReasonDefault) };
        var risk = new Badge { Tone = approval.Risk == ToolRisks.External ? BadgeTone.Warning : BadgeTone.Info, Content = RiskText(approval.Risk) };
        var content = new StackPanel
        {
            MinWidth = 420,
            Spacing = 14,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { risk, Muted(approval.Detail) },
                },
                Section(AgentsLangResources.Get(AgentsLangResourceKind.ApproveArgs)),
                new DescriptionList { ItemsSource = approval.Arguments, LabelWidth = 64 },
                reason,
            },
        };

        var dialog = new Dialog { Title = AgentsLangResources.Get(AgentsLangResourceKind.ApproveTitle), Description = AgentsLangResources.Get(AgentsLangResourceKind.ApproveHint), Content = content };
        var row = (StackPanel)Dialog.BuildActions(
        [
            new DialogAction(AgentsLangResources.Get(AgentsLangResourceKind.Cancel), "cancel", ButtonVariant.Secondary),
            new DialogAction(AgentsLangResources.Get(AgentsLangResourceKind.Reject), "reject", ButtonVariant.Danger),
        ]);
        var hold = new HoldToConfirm
        {
            Label = AgentsLangResources.Get(AgentsLangResourceKind.ApproveHold),
            ConfirmedLabel = AgentsLangResources.Get(AgentsLangResourceKind.ApproveDone),
            HoldDuration = TimeSpan.FromMilliseconds(900),
            Tone = HoldToConfirmTone.Accent,
            Icon = TKCode.Arc.Icons.Lucide.Lucide.Geometry(TKCode.Arc.Icons.Lucide.LucideIconKind.ShieldCheck),
        };
        hold.Confirmed += (_, _) => dialog.Close("approve");
        row.Children.Add(hold);
        dialog.Actions = row;

        var result = await dialog.ShowAsync(owner);
        if (result is not ("approve" or "reject"))
        {
            return false;
        }
        try
        {
            await service.DecideAsync(approval.Id, result is "approve", result is "reject" ? reason.Text?.Trim() : null);
            return true;
        }
        catch (ServiceException)
        {
            return false;
        }
    }

    internal static string RiskText(string risk) => AgentsLangResources.Get(risk switch
    {
        ToolRisks.External => AgentsLangResourceKind.RiskExternal,
        ToolRisks.Write => AgentsLangResourceKind.RiskWrite,
        _ => AgentsLangResourceKind.RiskRead,
    });

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        block.Classes.Add("muted");
        return block;
    }

    private static TextBlock Section(string text)
    {
        var block = new TextBlock { Text = text };
        block.Classes.Add("section");
        block.Classes.Add("secondary");
        return block;
    }
}
