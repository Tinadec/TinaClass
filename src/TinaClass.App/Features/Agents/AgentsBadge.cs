using TinaClass.App.Common.Navigation;

namespace TinaClass.App.Features.Agents;

/// <summary>The number of approvals that wait, beside "Agents" in the sidebar.</summary>
public sealed class AgentsBadge : IBadgeSource
{
    private readonly IAgentsService _service;

    public AgentsBadge(IAgentsService service)
    {
        _service = service;
        _service.ApprovalsChanged += async (_, _) => await RefreshAsync();
    }

    public string PageKey => AppPages.Agents;

    public string? Text { get; private set; }

    public event EventHandler? Changed;

    public async Task RefreshAsync()
    {
        try
        {
            var count = (await _service.PendingApprovalsAsync()).Count;
            Text = count == 0 ? null : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Common.Api.ServiceException)
        {
            Text = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
