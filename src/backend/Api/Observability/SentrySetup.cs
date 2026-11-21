using Api.Identity;
using FluentValidation;
using Sentry;

namespace Api.Observability;

/// <summary>
/// Optional error reporting to Sentry. Off unless <c>Sentry:Dsn</c> is configured (Azure app setting
/// <c>Sentry__Dsn</c>), so local runs and tests send nothing. Only unexpected failures (HTTP 500 and
/// errors logged by background work) are reported; validation, not-found, conflict and permission
/// responses are normal and never sent. No personal data: no IP address, cookies, headers, query
/// strings or request bodies, and the user is identified by id only.
/// </summary>
public static class SentrySetup
{
    public const string SectionName = "Sentry";

    public static bool IsEnabled(IConfiguration configuration) => !string.IsNullOrWhiteSpace(configuration[$"{SectionName}:Dsn"]);

    public static void Configure(WebApplicationBuilder builder)
    {
        if (!IsEnabled(builder.Configuration)) return;
        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = builder.Configuration[$"{SectionName}:Dsn"];
            options.Environment = builder.Configuration[$"{SectionName}:Environment"] ?? builder.Environment.EnvironmentName.ToLowerInvariant();
            options.Release = builder.Configuration[$"{SectionName}:Release"];
            options.SendDefaultPii = false;
            options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
            // Errors only: no performance tracing, so the free quota is spent on real problems.
            options.TracesSampleRate = 0;
            options.ReportAssembliesMode = ReportAssembliesMode.None;
            // Breadcrumbs come from Serilog (see AddSerilog below), not a second logging pipeline.
            options.MinimumBreadcrumbLevel = LogLevel.None;
            options.MinimumEventLevel = LogLevel.None;
            options.SetBeforeSend(Scrub);
        });
    }

    /// <summary>Drops expected outcomes and strips anything that could identify a customer.</summary>
    public static SentryEvent? Scrub(SentryEvent e, SentryHint _)
    {
        if (IsExpected(e.Exception)) return null;
        e.Request.Cookies = null;
        e.Request.QueryString = null;
        e.Request.Data = null;
        e.Request.Headers.Clear();
        e.Request.Env.Clear();
        if (e.User is { } user)
        {
            user.IpAddress = null;
            user.Email = null;
            user.Username = null;
        }
        e.ServerName = null;
        return e;
    }

    public static bool IsExpected(Exception? exception) => exception is ValidationException
        or Application.Common.NotFoundException
        or Application.Common.ConflictException
        or Application.Common.AuthenticationFailedException
        or Application.Common.ForbiddenException
        or Application.Common.RateLimitedException
        or BadHttpRequestException
        or OperationCanceledException;

    /// <summary>Reports an unhandled request failure with the ids staff can match against the API log.</summary>
    public static void CaptureRequestFailure(HttpContext context, Exception exception, string traceId)
    {
        if (!SentrySdk.IsEnabled) return;
        SentrySdk.CaptureException(exception, scope =>
        {
            if (context.Items["CorrelationId"] is string correlationId) scope.SetTag("correlation_id", correlationId);
            scope.SetTag("trace_id", traceId);
            scope.SetTag("route", context.GetEndpoint()?.DisplayName ?? "unknown");
            if (context.User.UserId() is { } userId) scope.User = new SentryUser { Id = userId.ToString() };
        });
    }
}
