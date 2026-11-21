using Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Api.Identity;

public static class RefreshCookie
{
    public const string Name = "sp_refresh";
    public const string Path = "/api/auth";

    public static void Write(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name, token, Options(expires));

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}

/// <summary>
/// Protects the cookie-authenticated endpoints (refresh, logout) against cross-site requests:
/// the Origin must be one of the configured admin origins and a custom header must be present,
/// which a browser only sends cross-origin after a CORS preflight that other sites fail.
/// The SameSite=Strict cookie is the third layer (ADR 0003).
/// </summary>
public sealed class RequireSameOriginAttribute : Attribute, IAsyncResourceFilter
{
    public const string Header = "X-CSRF-Token";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var allowed = context.HttpContext.RequestServices.GetRequiredService<IOptions<CorsSettings>>().Value.Origins;
        var origin = request.Headers.Origin.ToString();
        if (request.Headers[Header] != "1" || origin.Length == 0 || !allowed.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Request blocked.", Detail = "This request must come from the admin app." }) { StatusCode = 403 };
            return;
        }
        await next();
    }
}
