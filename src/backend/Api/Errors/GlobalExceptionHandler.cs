using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Api.Errors;

public sealed class GlobalExceptionHandler(IProblemDetailsService problems, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails detail;
        if (exception is ValidationException validation)
        {
            detail = new HttpValidationProblemDetails(validation.Errors.GroupBy(x => x.PropertyName)
                .ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).Distinct().ToArray()))
            { Status = 400, Title = "One or more validation errors occurred." };
        }
        else if (exception is BadHttpRequestException)
        {
            detail = new ProblemDetails { Status = 400, Title = "The request could not be read." };
        }
        else
        {
            detail = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." };
            // Never serialize the exception/message: provider failures can contain secrets.
            logger.LogError("Unhandled {ExceptionType}; trace {TraceId}", exception.GetType().Name, Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
        }
        context.Response.StatusCode = detail.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = detail });
    }
}
