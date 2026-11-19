using TinaClass.App.Common.Api.Localization;
using TinaClass.Contracts.Common;

namespace TinaClass.App.Common.Api;

/// <summary>
/// A call to the API that did not work, with the error <see cref="Code"/> (<see cref="ErrorCodes"/>), or <see cref="Unreachable"/>
/// when there was no answer. <see cref="Exception.Message"/> is already in the app's language, ready to show.
/// </summary>
public sealed class ServiceException(string code, IReadOnlyDictionary<string, string[]>? fieldErrors = null, Exception? inner = null)
    : Exception(ErrorText.For(code), inner)
{
    /// <summary>The code for "the server did not answer".</summary>
    public const string Unreachable = "unreachable";

    /// <summary>The code for "the session ended" (the refresh token was refused).</summary>
    public const string SessionExpired = "session_expired";

    public string Code { get; } = code;

    /// <summary>For <see cref="ErrorCodes.Validation"/>: the fields and what is wrong with them.</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string[]>();
}

/// <summary>
/// The text shown for an error code. The codes of the core (sign-in, access, conflicts, budget, models) are here; a feature that has
/// codes of its own (a duplicate document, a taken command) registers them with <see cref="Register"/> from its <c>Add…</c>, so that
/// leaving the feature out leaves no text behind.
/// </summary>
public static class ErrorText
{
    private static readonly Dictionary<string, Func<string>> s_registered = [];

    /// <summary>Adds the text of a feature's error code.</summary>
    public static void Register(string code, Func<string> text) => s_registered[code] = text;

    public static string For(string code) => s_registered.TryGetValue(code, out var text) ? text() : code switch
    {
        ErrorCodes.AccountLocked => ApiLangResources.Get(ApiLangResourceKind.ErrorAccountLocked),
        ErrorCodes.BudgetExceeded => ApiLangResources.Get(ApiLangResourceKind.ErrorBudgetExceeded),
        ErrorCodes.NoModel => ApiLangResources.Get(ApiLangResourceKind.ErrorNoModel),
        ErrorCodes.ProviderFailed => ApiLangResources.Get(ApiLangResourceKind.ErrorProviderFailed),
        ErrorCodes.InvalidState => ApiLangResources.Get(ApiLangResourceKind.ErrorInvalidState),
        ErrorCodes.KeyRequired => ApiLangResources.Get(ApiLangResourceKind.ErrorKeyRequired),
        ErrorCodes.InvalidCredentials => ApiLangResources.Get(ApiLangResourceKind.ErrorInvalidCredentials),
        ErrorCodes.Unauthorized or ErrorCodes.InvalidRefreshToken or ServiceException.SessionExpired => ApiLangResources.Get(ApiLangResourceKind.ErrorSessionExpired),
        ErrorCodes.Forbidden => ApiLangResources.Get(ApiLangResourceKind.ErrorForbidden),
        ErrorCodes.NotFound => ApiLangResources.Get(ApiLangResourceKind.ErrorNotFound),
        ErrorCodes.Conflict => ApiLangResources.Get(ApiLangResourceKind.ErrorConflict),
        ErrorCodes.Validation => ApiLangResources.Get(ApiLangResourceKind.ErrorValidation),
        ErrorCodes.RateLimited => ApiLangResources.Get(ApiLangResourceKind.ErrorRateLimited),
        ServiceException.Unreachable => ApiLangResources.Format(ApiLangResourceKind.ErrorUnreachable, ApiSettings.Current.Host),
        _ => ApiLangResources.Get(ApiLangResourceKind.ErrorGeneric),
    };
}
