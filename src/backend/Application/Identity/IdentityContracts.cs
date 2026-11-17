using Application.Catalog;

namespace Application.Identity;

// ---- Sign-in ----
public sealed record LoginRequest(string Email, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ClientInfo(string? Ip, string? UserAgent);

public sealed record SessionUser(Guid Id, string Email, string DisplayName, bool MustChangePassword, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, SessionUser User);

/// <summary>Result of a successful sign-in or refresh: the response body plus the raw refresh token for the cookie.</summary>
public sealed record AuthSession(AuthResponse Response, string RefreshToken, DateTimeOffset RefreshExpiresAt);

public sealed record MenuNode(string Key, string Title, string? Path, string? Icon, IReadOnlyList<MenuNode> Children);

/// <summary>What one user may do right now. Cached briefly; see ADR 0003.</summary>
public sealed record AccessSnapshot(Guid UserId, bool IsActive, string StampHash, bool IsSuperAdmin, IReadOnlySet<string> Permissions)
{
    public bool Has(string permission) => IsActive && (IsSuperAdmin || Permissions.Contains(permission));
}

public sealed record AccessTokenRequest(Guid UserId, Guid FamilyId, string StampHash);
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

// ---- Users ----
public sealed record UserListRequest(int Page = 1, int PageSize = 20, string? Sort = null, string? Q = null, bool IncludeDeleted = false);
public sealed record UserSummaryDto(Guid Id, string Email, string DisplayName, bool IsActive, bool IsLockedOut, bool IsDeleted, IReadOnlyList<string> Roles, DateTimeOffset? LastLoginAt);
public sealed record UserDto(Guid Id, string Email, string DisplayName, bool IsActive, bool IsLockedOut, bool IsDeleted, bool MustChangePassword,
    IReadOnlyList<RoleRef> Roles, DateTimeOffset? LastLoginAt, DateTimeOffset UpdatedAt, uint Version);
public sealed record RoleRef(Guid Id, string Name);
public sealed record UserCreateInput(string Email, string DisplayName, string TemporaryPassword, IReadOnlyList<Guid>? RoleIds);
public sealed record UserUpdateInput(string Email, string DisplayName, bool IsActive, uint Version);
public sealed record ResetPasswordInput(string TemporaryPassword);
public sealed record UserRolesInput(IReadOnlyList<Guid> RoleIds);

public enum OverrideState { Inherit = 0, Allow = 1, Deny = 2 }
public sealed record PermissionOverrideInput(string Key, OverrideState State);
public sealed record UserPermissionsInput(IReadOnlyList<PermissionOverrideInput> Overrides);

/// <summary>One permission as it applies to a user: what the roles give, the override, and the result.</summary>
public sealed record UserPermissionState(string Key, string Group, string Description, bool FromRoles, OverrideState Override, bool Effective);
public sealed record UserPermissionMenu(string Key, string Title, string? Permission, IReadOnlyList<UserPermissionMenu> Children);
public sealed record UserPermissionsDto(Guid UserId, bool IsSuperAdmin, IReadOnlyList<UserPermissionMenu> Menus, IReadOnlyList<UserPermissionState> Permissions);

// ---- Roles ----
public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, bool PermissionsLocked, int UserCount, IReadOnlyList<string> Permissions, uint Version);
public sealed record RoleInput(string Name, string? Description, IReadOnlyList<string> Permissions, uint? Version);

public interface IAuthService
{
    Task<AuthSession> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct);
    Task<AuthSession> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task LogoutAllAsync(Guid userId, CancellationToken ct);
    Task<AuthSession> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, ClientInfo client, CancellationToken ct);
    Task<SessionUser> GetSessionUserAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<MenuNode>> GetMenusAsync(Guid userId, CancellationToken ct);
}

public interface IAccessService
{
    Task<AccessSnapshot> GetAsync(Guid userId, CancellationToken ct);
    void Invalidate(Guid userId);
    void InvalidateAll();
}

/// <summary>Issues signed access tokens. Implemented in the API layer, which owns JWT settings.</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(AccessTokenRequest request);
}

public interface IUserAdminService
{
    Task<PagedResult<UserSummaryDto>> ListAsync(UserListRequest request, CancellationToken ct);
    Task<UserDto> GetAsync(Guid id, CancellationToken ct);
    Task<UserDto> CreateAsync(UserCreateInput input, CancellationToken ct);
    Task<UserDto> UpdateAsync(Guid id, UserUpdateInput input, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<UserDto> RestoreAsync(Guid id, CancellationToken ct);
    Task ResetPasswordAsync(Guid id, ResetPasswordInput input, CancellationToken ct);
    Task<UserDto> UnlockAsync(Guid id, CancellationToken ct);
    Task<UserDto> SetRolesAsync(Guid id, UserRolesInput input, CancellationToken ct);
    Task<UserPermissionsDto> GetPermissionsAsync(Guid id, CancellationToken ct);
    Task<UserPermissionsDto> SetPermissionsAsync(Guid id, UserPermissionsInput input, CancellationToken ct);
}

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct);
    Task<RoleDto> CreateAsync(RoleInput input, CancellationToken ct);
    Task<RoleDto> UpdateAsync(Guid id, RoleInput input, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
