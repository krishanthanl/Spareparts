using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Api.Identity;

public static class Permissions
{
    public const string ClaimType = "permission";
    public const string CatalogManage = "catalog.manage";
}

/// <summary>
/// Placeholder until Phase 1 adds JWT sign-in. It never authenticates anyone, so every protected
/// endpoint fails closed with 401. Phase 1 replaces this scheme; policies stay the same.
/// </summary>
public sealed class NoSignInYetHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "NoSignInYet";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
