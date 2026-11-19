using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Common.Http;

/// <summary>
/// Writes an <see cref="ApiException"/> as RFC 9457 problem details with <c>code</c> (and <c>errors</c>). A write that lost a
/// race (the app-level <c>Version</c> of a row changed under it) becomes 409 <c>conflict</c>: the caller reloads and retries. A request
/// that cannot be read becomes 400 <c>validation</c>. Anything else is left to the default handler (500, no details in the response).
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var api = exception switch
        {
            ApiException known => known,
            DbUpdateConcurrencyException => ApiException.Conflict(ErrorCodes.Conflict, "The record was changed by someone else. Reload it and try again."),
            // A body that is not JSON, or missing: Kestrel answers 400 itself in Production, but in Development the framework throws instead.
            BadHttpRequestException bad => new ApiException(bad.StatusCode, ErrorCodes.Validation, "The request could not be read."),
            _ => null,
        };
        if (api is null)
        {
            return false;
        }
        context.Response.StatusCode = api.Status;
        var details = new ProblemDetails { Status = api.Status, Title = api.Code, Detail = api.Message };
        details.Extensions["code"] = api.Code;
        if (api.Errors is not null)
        {
            details.Extensions["errors"] = api.Errors;
        }
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = details, Exception = exception });
    }
}
