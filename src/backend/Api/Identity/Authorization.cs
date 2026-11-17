using System.Security.Claims;
using Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Api.Identity;

public static class Permissions
{
    public const string CatalogManage = PermissionKeys.CatalogManage;
}

public static class AuthClaims
{
    public const string Subject = "sub";
    public const string Session = "sid";
    public const string Stamp = "stamp";

    public static Guid? UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(Subject) ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

/// <summary>Requires one permission key. The key is also the policy name.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Checks permissions on the server for every request, from the cached access snapshot
/// (never from the menu, and never from claims in the token).
/// </summary>
public sealed class PermissionHandler(IAccessService access) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.UserId() is not { } userId) return;
        var snapshot = await access.GetAsync(userId, CancellationToken.None);
        if (snapshot.Has(requirement.Permission)) context.Succeed(requirement);
    }
}

public static class AuthorizationSetup
{
    public static AuthorizationBuilder AddPermissionPolicies(this AuthorizationBuilder builder)
    {
        foreach (var permission in PermissionKeys.All)
            builder.AddPolicy(permission.Key, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Key)));
        return builder;
    }
}

public static class ControllerPermissionExtensions
{
    /// <summary>Extra permission check inside an action, e.g. for viewing deleted records.</summary>
    public static async Task RequirePermissionAsync(this Microsoft.AspNetCore.Mvc.ControllerBase controller, string permission)
    {
        var authorization = controller.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        if (!(await authorization.AuthorizeAsync(controller.User, permission)).Succeeded)
            throw new Application.Common.ForbiddenException("You do not have permission for this.");
    }
}
