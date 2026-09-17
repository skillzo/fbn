using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace NovaWallet.Api.Errors;

public class ErrorHandler(ILogger<ErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not AppException appEx)
        {
            logger.LogError(exception, "Unhandled exception");
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred.",
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            };
            httpContext.Response.StatusCode = problem.Status.Value;
            await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            return true;
        }

        var details = new ProblemDetails
        {
            Status = appEx.Status,
            Title = appEx.Message,
            Extensions =
            {
                ["code"] = appEx.Code,
                ["traceId"] = httpContext.TraceIdentifier
            }
        };
        httpContext.Response.StatusCode = appEx.Status;
        await httpContext.Response.WriteAsJsonAsync(details, cancellationToken);
        return true;
    }
}
