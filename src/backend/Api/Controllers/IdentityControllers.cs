using Api.Identity;
using Application.Catalog;
using Application.Common;
using Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    public const string LoginRateLimit = "login";

    private ClientInfo Client => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
    private Guid UserId => User.UserId() ?? throw new AuthenticationFailedException(AuthenticationFailedException.SessionEnded);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimit)]
    public async Task<AuthResponse> Login(LoginRequest request, CancellationToken ct) => Respond(await auth.LoginAsync(request, Client, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [RequireSameOrigin]
    public async Task<AuthResponse> Refresh(CancellationToken ct)
    {
        try
        {
            return Respond(await auth.RefreshAsync(RefreshCookie.Read(Request) ?? "", Client, ct));
        }
        catch (AuthenticationFailedException)
        {
            RefreshCookie.Clear(Response);
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [RequireSameOrigin]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(RefreshCookie.Read(Request), ct);
        RefreshCookie.Clear(Response);
        return NoContent();
    }

    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await auth.LogoutAllAsync(UserId, ct);
        RefreshCookie.Clear(Response);
        return NoContent();
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<AuthResponse> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        Respond(await auth.ChangePasswordAsync(UserId, request, Client, ct));

    [HttpGet("me")]
    [Authorize]
    public Task<SessionUser> Me(CancellationToken ct) => auth.GetSessionUserAsync(UserId, ct);

    [HttpGet("menus")]
    [Authorize]
    public Task<IReadOnlyList<MenuNode>> Menus(CancellationToken ct) => auth.GetMenusAsync(UserId, ct);

    private AuthResponse Respond(AuthSession session)
    {
        RefreshCookie.Write(Response, session.RefreshToken, session.RefreshExpiresAt);
        Response.Headers.CacheControl = "no-store";
        return session.Response;
    }
}

[ApiController]
[Route("api/users")]
[Authorize(Policy = PermissionKeys.UsersView)]
public sealed class UsersController(IUserAdminService users) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<UserSummaryDto>> List([FromQuery] UserListRequest request, CancellationToken ct)
    {
        if (request.IncludeDeleted) await this.RequirePermissionAsync(PermissionKeys.UsersManage);
        return await users.ListAsync(request, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<UserDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public async Task<ActionResult<UserDto>> Create(UserCreateInput input, CancellationToken ct)
    {
        var created = await users.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public Task<UserDto> Update(Guid id, UserUpdateInput input, CancellationToken ct) => users.UpdateAsync(id, input, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public Task<UserDto> Restore(Guid id, CancellationToken ct) => users.RestoreAsync(id, ct);

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordInput input, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, input, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unlock")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public Task<UserDto> Unlock(Guid id, CancellationToken ct) => users.UnlockAsync(id, ct);

    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public Task<UserDto> SetRoles(Guid id, UserRolesInput input, CancellationToken ct) => users.SetRolesAsync(id, input, ct);

    [HttpGet("{id:guid}/permissions")]
    [Authorize(Policy = PermissionKeys.PermissionsManage)]
    public Task<UserPermissionsDto> GetPermissions(Guid id, CancellationToken ct) => users.GetPermissionsAsync(id, ct);

    [HttpPut("{id:guid}/permissions")]
    [Authorize(Policy = PermissionKeys.PermissionsManage)]
    public Task<UserPermissionsDto> SetPermissions(Guid id, UserPermissionsInput input, CancellationToken ct) => users.SetPermissionsAsync(id, input, ct);
}

[ApiController]
[Route("api/roles")]
[Authorize(Policy = PermissionKeys.RolesManage)]
public sealed class RolesController(IRoleService roles) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RoleDto>> List(CancellationToken ct) => roles.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create(RoleInput input, CancellationToken ct) => StatusCode(201, await roles.CreateAsync(input, ct));

    [HttpPut("{id:guid}")]
    public Task<RoleDto> Update(Guid id, RoleInput input, CancellationToken ct) => roles.UpdateAsync(id, input, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await roles.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/permissions")]
public sealed class PermissionsController : ControllerBase
{
    /// <summary>Role list for user forms and the permission catalogue for role editing.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionKeys.RolesManage)]
    public IReadOnlyList<PermissionDefinition> List() => PermissionKeys.All;

    [HttpGet("/api/roles/options")]
    [Authorize(Policy = PermissionKeys.UsersManage)]
    public async Task<IReadOnlyList<RoleRef>> RoleOptions([FromServices] IRoleService roles, CancellationToken ct) =>
        (await roles.ListAsync(ct)).Select(r => new RoleRef(r.Id, r.Name)).ToList();
}
