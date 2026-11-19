using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Agents.Data;
using TinaClass.Api.Features.Models;
using TinaClass.Contracts.Agents;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Features.Agents;

public static class AgentsAudit
{
    public const string AgentCreated = "agents.agent_created";
    public const string AgentUpdated = "agents.agent_updated";
    public const string AgentPublished = "agents.agent_published";
    public const string AgentDeleted = "agents.agent_deleted";
    public const string ServerAdded = "agents.server_added";
    public const string ServerRemoved = "agents.server_removed";
}

/// <summary>Agents, their tools and knowledge bases, and the tool servers (design 4.3). Visible: workspace agents and your own private ones; changed: your own, or anyone's by an owner or administrator.</summary>
public sealed class AgentsService(AgentsDbContext db, IActorContext actors, IAuditLog audit, ToolDiscovery discovery, TimeProvider time)
{
    internal async Task<Agent> FindAsync(Guid id, bool forWrite, bool tracking, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var query = tracking ? db.Agents.AsQueryable() : db.Agents.AsNoTracking();
        var agent = await query.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent is null || (agent.Visibility == "private" && agent.OwnerId != actor.Id))
        {
            throw ApiException.NotFound("The agent");
        }
        if (forWrite && agent.OwnerId != actor.Id && actor.RoleId is not (Roles.Owner or Roles.Admin))
        {
            throw ApiException.Forbidden("Only the owner of an agent, or an administrator, can change it.");
        }
        return agent;
    }

    public async Task<IReadOnlyList<AgentDto>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var id = actor.Id;
        var agents = await db.Agents.AsNoTracking().Where(a => a.Visibility != "private" || a.OwnerId == id).OrderBy(a => a.Name).ThenBy(a => a.Id).Take(200).ToListAsync(cancellationToken);
        return await MapAsync(agents, cancellationToken);
    }

    public async Task<AgentDto> GetAsync(Guid id, CancellationToken cancellationToken) => (await MapAsync([await FindAsync(id, false, false, cancellationToken)], cancellationToken))[0];

    public async Task<AgentDto> CreateAsync(SaveAgentRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        await ValidateAsync(request, cancellationToken);
        var agent = new Agent { Id = Guid.NewGuid(), OwnerId = actor.Id ?? Guid.Empty, Name = "", UpdatedAt = time.GetUtcNow() };
        Apply(agent, request);
        db.Agents.Add(agent);
        await ReplaceLinksAsync(agent.Id, request, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AgentsAudit.AgentCreated, "agent", agent.Id.ToString(), agent.Name), cancellationToken);
        return await GetAsync(agent.Id, cancellationToken);
    }

    public async Task<AgentDto> UpdateAsync(Guid id, SaveAgentRequest request, CancellationToken cancellationToken)
    {
        var agent = await FindAsync(id, forWrite: true, tracking: true, cancellationToken);
        await ValidateAsync(request, cancellationToken);
        db.ExpectVersion(agent, request.Version);
        Apply(agent, request);
        agent.UpdatedAt = time.GetUtcNow();
        await ReplaceLinksAsync(id, request, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AgentsAudit.AgentUpdated, "agent", id.ToString(), agent.Name), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<AgentDto> PublishAsync(Guid id, CancellationToken cancellationToken)
    {
        var agent = await FindAsync(id, forWrite: true, tracking: true, cancellationToken);
        if (string.IsNullOrWhiteSpace(agent.Instruction))
        {
            throw ApiException.Invalid("instruction", "Write the agent's instruction before publishing it.");
        }
        agent.State = "published";
        agent.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AgentsAudit.AgentPublished, "agent", id.ToString(), agent.Name), cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var agent = await FindAsync(id, forWrite: true, tracking: true, cancellationToken);
        if (await db.Runs.AnyAsync(r => r.AgentId == id && (r.State == RunStates.Queued || r.State == RunStates.Running || r.State == RunStates.WaitingApproval), cancellationToken))
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "The agent has a run in progress. Cancel it first.");
        }
        await db.Approvals.Where(a => db.Runs.Any(r => r.Id == a.RunId && r.AgentId == id)).ExecuteDeleteAsync(cancellationToken);
        db.Agents.Remove(agent);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AgentsAudit.AgentDeleted, "agent", id.ToString(), agent.Name), cancellationToken);
    }

    private void Apply(Agent agent, SaveAgentRequest r)
    {
        agent.Name = r.Name.Trim();
        agent.Description = r.Description?.Trim() ?? "";
        agent.Instruction = r.Instruction ?? "";
        agent.ModelCode = r.ModelCode?.Trim() ?? "";
        agent.MaxSteps = r.MaxSteps;
        agent.MaxCost = r.MaxCost;
        agent.Visibility = r.Visibility;
        agent.Schedule = string.IsNullOrWhiteSpace(r.Schedule) ? null : r.Schedule.Trim();
        agent.Icon = string.IsNullOrWhiteSpace(r.Icon) ? "bot" : r.Icon.Trim();
    }

    private async Task ValidateAsync(SaveAgentRequest r, CancellationToken cancellationToken)
    {
        var validator = new Validator().Text("name", r.Name, 80).Text("description", r.Description, 400, required: false).Text("modelCode", r.ModelCode, 120, required: false)
                                       .Text("schedule", r.Schedule, 60, required: false).Text("icon", r.Icon, 24, required: false);
        if ((r.Instruction?.Length ?? 0) > 8_000)
        {
            validator.Add("instruction", "At most 8000 characters.");
        }
        if (r.MaxSteps is < 1 or > 50)
        {
            validator.Add("maxSteps", "Use 1 to 50 steps.");
        }
        if (r.MaxCost is < 0 or > 1_000_000_000)
        {
            validator.Add("maxCost", "Use zero (no limit) or a positive amount in minor units.");
        }
        if (r.Visibility is not ("private" or "workspace"))
        {
            validator.Add("visibility", "Use private or workspace.");
        }
        if (r.BaseIds is { Count: > 20 })
        {
            validator.Add("baseIds", "At most 20 knowledge bases.");
        }
        if (r.Tools is not null)
        {
            if (r.Tools.Count > 40 || r.Tools.Select(t => t.ToolId).Distinct().Count() != r.Tools.Count)
            {
                validator.Add("tools", "Give each tool once, at most 40.");
            }
            if (r.Tools.Any(t => t.Approval is not (ToolApprovals.Auto or ToolApprovals.Ask or ToolApprovals.Always)))
            {
                validator.Add("tools", "Use auto, ask or always for a tool's approval.");
            }
            var ids = r.Tools.Select(t => t.ToolId).ToList();
            if (await db.Tools.CountAsync(t => ids.Contains(t.Id), cancellationToken) != ids.Count)
            {
                validator.Add("tools", "A tool does not exist.");
            }
        }
        validator.ThrowIfInvalid();
    }

    private async Task ReplaceLinksAsync(Guid agentId, SaveAgentRequest r, CancellationToken cancellationToken)
    {
        if (r.Tools is not null)
        {
            db.AgentTools.RemoveRange(await db.AgentTools.Where(l => l.AgentId == agentId).ToListAsync(cancellationToken));
            db.AgentTools.AddRange(r.Tools.Select((t, i) => new AgentToolLink { Id = Guid.NewGuid(), AgentId = agentId, ToolId = t.ToolId, Enabled = t.Enabled, Approval = t.Approval, Sort = i }));
        }
        if (r.BaseIds is not null)
        {
            db.AgentBases.RemoveRange(await db.AgentBases.Where(l => l.AgentId == agentId).ToListAsync(cancellationToken));
            db.AgentBases.AddRange(r.BaseIds.Distinct().Select(b => new AgentBaseLink { Id = Guid.NewGuid(), AgentId = agentId, BaseId = b }));
        }
    }

    private async Task<IReadOnlyList<AgentDto>> MapAsync(IReadOnlyList<Agent> agents, CancellationToken cancellationToken)
    {
        var ids = agents.Select(a => a.Id).ToList();
        var links = await (from l in db.AgentTools.AsNoTracking() join t in db.Tools.AsNoTracking() on l.ToolId equals t.Id where ids.Contains(l.AgentId) orderby l.Sort, l.Id
                           select new { l.AgentId, l.ToolId, t.Name, t.Risk, l.Enabled, l.Approval }).ToListAsync(cancellationToken);
        var bases = await db.AgentBases.AsNoTracking().Where(l => ids.Contains(l.AgentId)).OrderBy(l => l.BaseId).ToListAsync(cancellationToken);
        var live = await db.Runs.AsNoTracking().Where(r => ids.Contains(r.AgentId) && r.State != RunStates.Succeeded && r.State != RunStates.Failed && r.State != RunStates.Cancelled)
                               .OrderByDescending(r => r.StartedAt).ThenBy(r => r.Id).ToListAsync(cancellationToken);
        return [.. agents.Select(a => new AgentDto(a.Id, a.Name, a.Description, a.Instruction, a.ModelCode, a.MaxSteps, a.MaxCost, a.Visibility, a.State, a.Schedule, a.Icon,
            [.. links.Where(l => l.AgentId == a.Id).Select(l => new AgentToolRef(l.ToolId, l.Name, l.Risk, l.Enabled, l.Approval))], [.. bases.Where(b => b.AgentId == a.Id).Select(b => b.BaseId)],
            a.RunCount, a.RunCount == 0 ? 0 : a.SuccessCount * 100 / a.RunCount, live.FirstOrDefault(r => r.AgentId == a.Id)?.State, a.Version))];
    }

    // ---- tool servers and tools

    public async Task<IReadOnlyList<ToolServerDto>> ServersAsync(CancellationToken cancellationToken)
    {
        var servers = await db.Servers.AsNoTracking().OrderBy(s => s.Kind != "builtin").ThenBy(s => s.Name).ThenBy(s => s.Id).ToListAsync(cancellationToken);
        var counts = await db.Tools.AsNoTracking().GroupBy(t => t.ServerId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return [.. servers.Select(s => ToDto(s, counts.GetValueOrDefault(s.Id)))];
    }

    public async Task<ToolServerDto> AddServerAsync(CreateToolServerRequest request, CancellationToken cancellationToken)
    {
        var validator = new Validator().Text("name", request.Name, 80).Text("endpoint", request.Endpoint, 300);
        if (request.Kind is not ("mcp" or "http"))
        {
            validator.Add("kind", "Use mcp or http (the built-in tools already exist).");
        }
        if (!string.IsNullOrWhiteSpace(request.Endpoint) && !(Uri.TryCreate(request.Endpoint.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            validator.Add("endpoint", "Use an absolute http or https address.");
        }
        validator.ThrowIfInvalid();
        var server = new ToolServer { Id = Guid.NewGuid(), Name = request.Name.Trim(), Kind = request.Kind, Endpoint = request.Endpoint.Trim() };
        db.Servers.Add(server);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AgentsAudit.ServerAdded, "tool_server", server.Id.ToString(), server.Name), cancellationToken);
        return ToDto(server, 0);
    }

    public async Task DeleteServerAsync(Guid id, CancellationToken cancellationToken)
    {
        var server = await db.Servers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw ApiException.NotFound("The tool server");
        if (server.Kind == "builtin")
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "The built-in tools cannot be removed.");
        }
        db.Servers.Remove(server);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AgentsAudit.ServerRemoved, "tool_server", id.ToString(), server.Name), cancellationToken);
    }

    /// <summary>Asks the server for its tools. New tools start as <c>write</c> (an administrator lowers a harmless one to read); existing ones keep their risk.</summary>
    public async Task<ToolServerDto> RefreshAsync(Guid id, CancellationToken cancellationToken)
    {
        var server = await db.Servers.SingleOrDefaultAsync(s => s.Id == id, cancellationToken) ?? throw ApiException.NotFound("The tool server");
        var result = await discovery.CheckAsync(server.Kind, server.Endpoint, cancellationToken);
        server.Status = result.Ok ? "ok" : "error";
        if (result.Ok)
        {
            server.LastSeenAt = time.GetUtcNow();
            var known = (await db.Tools.Where(t => t.ServerId == id).ToListAsync(cancellationToken)).ToDictionary(t => t.Name, StringComparer.Ordinal);
            var order = known.Count;
            foreach (var found in result.Tools.Where(t => !known.ContainsKey(t.Name)).Take(100))
            {
                db.Tools.Add(new Tool { Id = Guid.NewGuid(), ServerId = id, Name = found.Name, Description = found.Description, Risk = ToolRisks.Write, Sort = order++ });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(server, await db.Tools.CountAsync(t => t.ServerId == id, cancellationToken));
    }

    public async Task<IReadOnlyList<ToolDto>> ToolsAsync(CancellationToken cancellationToken)
    {
        var rows = await (from t in db.Tools.AsNoTracking() join s in db.Servers.AsNoTracking() on t.ServerId equals s.Id orderby s.Name, t.Sort, t.Id select new { t, ServerName = s.Name }).ToListAsync(cancellationToken);
        return [.. rows.Select(r => new ToolDto(r.t.Id, r.t.ServerId, r.ServerName, r.t.Name, r.t.Description, r.t.Risk))];
    }

    public async Task<ToolDto> UpdateToolAsync(Guid id, UpdateToolRequest request, CancellationToken cancellationToken)
    {
        var tool = await db.Tools.SingleOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw ApiException.NotFound("The tool");
        if (request.Risk is not null)
        {
            if (request.Risk is not (ToolRisks.Read or ToolRisks.Write or ToolRisks.External))
            {
                throw ApiException.Invalid("risk", "Use read, write or external.");
            }
            tool.Risk = request.Risk;
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AgentsAuditActions.ToolChanged, "tool", id.ToString(), tool.Name), cancellationToken);
        var server = await db.Servers.AsNoTracking().SingleAsync(s => s.Id == tool.ServerId, cancellationToken);
        return new ToolDto(tool.Id, tool.ServerId, server.Name, tool.Name, tool.Description, tool.Risk);
    }

    private static ToolServerDto ToDto(ToolServer s, int tools) => new(s.Id, s.Name, s.Kind, s.Endpoint, s.Status, tools, s.LastSeenAt);
}

public sealed record DiscoveredTool(string Name, string Description);

public sealed record DiscoveryResult(bool Ok, IReadOnlyList<DiscoveredTool> Tools);

/// <summary>
/// Checks a tool server and lists its tools. Built-in and <c>.example</c> servers (the sample workspace) answer without the network. A real
/// MCP server over HTTP is asked with the JSON-RPC <c>tools/list</c> call; anything else that answers counts as reachable.
/// </summary>
public sealed class ToolDiscovery(IHttpClientFactory http)
{
    public const string ClientName = "tools";

    public async Task<DiscoveryResult> CheckAsync(string kind, string endpoint, CancellationToken cancellationToken)
    {
        if (kind == "builtin" || ProviderRouting.IsDemoHost(endpoint) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return new DiscoveryResult(true, []);
        }
        try
        {
            using var client = http.CreateClient(ClientName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            if (kind != "mcp")
            {
                using var response = await client.GetAsync(endpoint, timeout.Token);
                return new DiscoveryResult((int)response.StatusCode < 500, []);
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json") };
            request.Headers.Accept.ParseAdd("application/json");
            using var answer = await client.SendAsync(request, timeout.Token);
            if (!answer.IsSuccessStatusCode)
            {
                return new DiscoveryResult(false, []);
            }
            using var doc = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(timeout.Token));
            var tools = new List<DiscoveredTool>();
            if (doc.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                        tools.Add(new DiscoveredTool(Cut(name, 80), Cut(description, 300)));
                    }
                }
            }
            return new DiscoveryResult(true, tools);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new DiscoveryResult(false, []);
        }
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}

/// <summary>What chat offers as tools: the tools of the servers that are reachable.</summary>
internal sealed class AgentToolCatalog(AgentsDbContext db) : IToolCatalog
{
    public async Task<IReadOnlyList<ToolInfo>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await (from t in db.Tools.AsNoTracking() join s in db.Servers.AsNoTracking() on t.ServerId equals s.Id where s.Status == "ok" orderby s.Name, t.Sort, t.Id select new ToolInfo(t.Id, t.Name, t.Risk)).ToListAsync(cancellationToken))];
}
