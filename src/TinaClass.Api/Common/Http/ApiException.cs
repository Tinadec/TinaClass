using TinaClass.Contracts.Common;

namespace TinaClass.Api.Common.Http;

/// <summary>
/// A request that cannot be served, turned into a problem details response by <see cref="ApiExceptionHandler"/>: the HTTP
/// status, an <see cref="ErrorCodes"/> code and, for validation, the problems by field.
/// </summary>
public sealed class ApiException(int status, string code, string? detail = null, IDictionary<string, string[]>? errors = null)
    : Exception(detail ?? code)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    public IDictionary<string, string[]>? Errors { get; } = errors;

    public static ApiException NotFound(string what) => new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"{what} was not found.");

    public static ApiException Forbidden(string? detail = null) =>
        new(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, detail ?? "You do not have permission to do this.");

    public static ApiException Conflict(string code, string detail) => new(StatusCodes.Status409Conflict, code, detail);

    public static ApiException Invalid(string field, string message) =>
        new(StatusCodes.Status400BadRequest, ErrorCodes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    public static ApiException Unauthorized(string code, string detail) => new(StatusCodes.Status401Unauthorized, code, detail);
}
