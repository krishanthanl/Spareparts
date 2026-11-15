using System.Diagnostics;

namespace Api.Observability;

public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-ID"].ToString();
        var id = Guid.TryParse(supplied, out var parsed) ? parsed.ToString("D") : Guid.NewGuid().ToString("D");
        context.Items["CorrelationId"] = id;
        context.Response.Headers["X-Correlation-ID"] = id;
        var start = Stopwatch.GetTimestamp();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
            // Log no query strings, request bodies, cookies, authorization or connection data.
            logger.LogInformation("HTTP {Method} completed {StatusCode} in {ElapsedMs}ms", context.Request.Method, context.Response.StatusCode, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}
