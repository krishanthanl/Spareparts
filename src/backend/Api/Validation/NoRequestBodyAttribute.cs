using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.Validation;

/// <summary>Posting commands take no client-controlled effective dates or other payload.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NoRequestBodyAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
            throw new ValidationException([new ValidationFailure("Body", "This action accepts no request body. Posting time is assigned by the server.")]);
    }
}
