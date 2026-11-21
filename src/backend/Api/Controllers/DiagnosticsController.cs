using Api.Validation;
using Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Lets an admin check that error reporting reaches Sentry after a deployment.</summary>
[ApiController]
[Route("api/diagnostics")]
[Authorize(Policy = PermissionKeys.RolesManage)]
public sealed class DiagnosticsController : ControllerBase
{
    /// <summary>Throws on purpose so a test error appears in Sentry. Answers 409 when Sentry is not configured.</summary>
    [HttpPost("sentry-test")]
    [NoRequestBody]
    public IActionResult SentryTest()
    {
        if (!Sentry.SentrySdk.IsEnabled)
            throw new Application.Common.ConflictException("Error reporting is not configured. Set the Sentry__Dsn app setting.");
        throw new SentryTestException();
    }
}

/// <summary>Marks the deliberate test error so it is easy to recognise and resolve in Sentry.</summary>
public sealed class SentryTestException() : Exception("Test error from POST /api/diagnostics/sentry-test. Safe to resolve.");
