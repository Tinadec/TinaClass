namespace TinaClass.Contracts.Common;

/// <summary>The API's paths, relative to its base address. Both the API and the app's client use these.</summary>
public static class ApiRoutes
{
    public const string Base = "/api/v1";

    public const string Auth = Base + "/auth";
    public const string Me = Base + "/me";
    public const string ApiKeys = Me + "/api-keys";

    public const string Conversations = Base + "/conversations";
    public const string Messages = Base + "/messages";
    public const string Folders = Base + "/folders";
    public const string Spaces = Base + "/spaces";
    public const string Search = Base + "/search";
    public const string Shares = Base + "/shares";
    public const string Shared = Base + "/shared";
    public const string Citations = Base + "/citations";
    public const string Providers = Base + "/providers";
    public const string Models = Base + "/models";
    public const string Knowledge = Base + "/knowledge";
    public const string Documents = Base + "/documents";
    public const string Agents = Base + "/agents";
    public const string Runs = Base + "/runs";
    public const string Approvals = Base + "/approvals";
    public const string ToolServers = Base + "/tool-servers";
    public const string Tools = Base + "/tools";
    public const string Flows = Base + "/flows";
    public const string FlowRuns = Base + "/flow-runs";
    public const string Prompts = Base + "/prompts";
    public const string Usage = Base + "/usage";
    public const string Members = Base + "/members";
    public const string Invites = Base + "/invites";
    public const string Audit = Base + "/audit";
}
