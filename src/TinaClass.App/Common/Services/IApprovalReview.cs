using Avalonia;

namespace TinaClass.App.Common.Services;

/// <summary>
/// Opens the approval dialog for a pending approval (design 4.3, "Approve": description, arguments, hold to confirm) from any page: the agents page,
/// a workflow's approval node. Agents registers the one implementation; a page that has no approvals to show does not ask for it.
/// </summary>
public interface IApprovalReview
{
    /// <summary>Shows the dialog for <paramref name="approvalId"/> (the first pending one when null) over <paramref name="owner"/>; true when the person approved or rejected it.</summary>
    Task<bool> ReviewAsync(Visual owner, Guid? approvalId = null);
}
