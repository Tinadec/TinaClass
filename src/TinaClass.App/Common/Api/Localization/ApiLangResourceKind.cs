using TKCode.Arc.Localization;

namespace TinaClass.App.Common.Api.Localization;

/// <summary>The API connection (one member per string; the .xlf files next to this one hold en-US, zh-CN and zh-TW).</summary>
[LanguageCatalog]
public enum ApiLangResourceKind
{
    SignInTitle,
    SignInTo,
    Email,
    Password,
    RememberMe,
    SignIn,
    SignOut,
    ErrorInvalidCredentials,
    ErrorSessionExpired,
    ErrorForbidden,
    ErrorNotFound,
    ErrorConflict,
    ErrorValidation,
    ErrorRateLimited,
    ErrorUnreachable,
    ErrorGeneric,
    ErrorAccountLocked,
    ErrorBudgetExceeded,
    ErrorNoModel,
    ErrorProviderFailed,
    ErrorInvalidState,
    ErrorKeyRequired,
}
