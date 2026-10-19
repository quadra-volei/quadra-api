using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Quadra.Api;

/// <summary>
/// A unique index refused a write because a concurrent request made the same one first: the
/// client gets 409 (try again or reload), never a 500. Any other exception falls through to the
/// default handler (500 ProblemDetails with no internals).
/// </summary>
internal sealed class UniqueViolationExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public UniqueViolationExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The request conflicts with a change made at the same time. Try again.",
            },
        });
    }
}
