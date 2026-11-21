using Domain.Common;

namespace Domain.Identity;

public enum UserType
{
    Staff = 1,
}

/// <summary>A staff login. Password hashing, lockout and security stamps are handled by ASP.NET Core Identity.</summary>
public sealed class User : AuditableEntity, ISoftDeletable
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? LockoutEnd { get; set; }
    public int AccessFailedCount { get; set; }
    public UserType UserType { get; set; } = UserType.Staff;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public uint Version { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public List<UserRole> Roles { get; } = [];
    public List<UserPermissionOverride> PermissionOverrides { get; } = [];
}

public sealed class Role : AuditableEntity
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string StoreKeeper = "StoreKeeper";
    public const string Cashier = "Cashier";

    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public uint Version { get; set; }
    public List<RolePermission> Permissions { get; } = [];
}

public sealed class UserRole : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}

public sealed class RolePermission : AuditableEntity
{
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
}

public enum PermissionEffect
{
    Allow = 1,
    Deny = 2,
}

/// <summary>Per-user exception to the permissions the user's roles give. Deny always wins.</summary>
public sealed class UserPermissionOverride : AuditableEntity
{
    public Guid UserId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public PermissionEffect Effect { get; set; }
}

/// <summary>Admin sidebar entry. Seeded from code; each entry needs one permission to be visible.</summary>
public sealed class Menu : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public string? RequiredPermission { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// A refresh token. Only the SHA-256 hash is stored. A family is one sign-in; every refresh
/// consumes the current token and adds its replacement to the same family.
/// </summary>
public sealed class RefreshToken : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset FamilyExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedById { get; set; }
    public string? CreatedIp { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>Append-only record of sign-in and account security events.</summary>
public sealed class SecurityEvent : AuditableEntity
{
    public Guid? UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string? Ip { get; set; }
    public string? Details { get; set; }
}

/// <summary>Permission keys already given to their default roles, so new keys reach existing roles once.</summary>
public sealed class PermissionSeed : AuditableEntity
{
    public string PermissionKey { get; set; } = string.Empty;
}
