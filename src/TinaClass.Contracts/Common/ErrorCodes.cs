namespace TinaClass.Contracts.Common;

/// <summary>
/// The <c>code</c> of an error response. Errors are RFC 9457 problem details; <c>code</c> says which one, so the app can
/// show its own text, and <c>errors</c> (on <see cref="Validation"/>) maps fields to reasons.
/// </summary>
public static class ErrorCodes
{
    public const string Validation = "validation";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string RateLimited = "rate_limited";
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountLocked = "account_locked";
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string BudgetExceeded = "budget_exceeded";
    public const string NoModel = "no_model";
    public const string ProviderFailed = "provider_failed";
    public const string InvalidState = "invalid_state";
    public const string KeyRequired = "key_required";
    public const string CommandTaken = "command_taken";
    public const string LastOwner = "last_owner";
    public const string DuplicateDocument = "duplicate_document";
}
