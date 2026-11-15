using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.Validation;

public sealed class FluentValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values.Where(x => x is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            var validators = context.HttpContext.RequestServices.GetServices(validatorType).Cast<IValidator>();
            foreach (var validator in validators)
            {
                var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
                if (!result.IsValid) throw new ValidationException(result.Errors);
            }
        }
        await next();
    }
}
