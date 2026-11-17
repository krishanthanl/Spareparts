using Application.Catalog;
using Application.Common;
using Application.Identity;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Identity;

public sealed class UserAdminService(AppDbContext db, UserManager<User> users, IAccessService access, ICurrentActor actor, TimeProvider clock) : IUserAdminService
{
    private const string EmailTaken = "A user with this email address already exists.";
    // Serializes changes that could remove the last active SuperAdmin, across all servers.
    private const long SuperAdminLock = 7_340_001;

    public async Task<PagedResult<UserSummaryDto>> ListAsync(UserListRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = request.IncludeDeleted ? db.Users.IgnoreQueryFilters() : db.Users;
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var pattern = LikePattern(request.Q);
            query = query.Where(x => EF.Functions.ILike(x.Email, pattern, "\\") || EF.Functions.ILike(x.DisplayName, pattern, "\\"));
        }
        query = request.Sort switch
        {
            "-name" => query.OrderByDescending(x => x.DisplayName),
            "email" => query.OrderBy(x => x.Email),
            "-email" => query.OrderByDescending(x => x.Email),
            "lastLogin" => query.OrderBy(x => x.LastLoginAt),
            "-lastLogin" => query.OrderByDescending(x => x.LastLoginAt),
            _ => query.OrderBy(x => x.DisplayName),
        };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new
            {
                x.Id, x.Email, x.DisplayName, x.IsActive, x.LockoutEnd, x.IsDeleted, x.LastLoginAt,
                Roles = x.Roles.OrderBy(r => r.Role!.Name).Select(r => r.Role!.Name).ToList(),
            }).ToListAsync(ct);
        return new(rows.Select(x => new UserSummaryDto(x.Id, x.Email, x.DisplayName, x.IsActive, x.LockoutEnd > now, x.IsDeleted, x.Roles, x.LastLoginAt)).ToList(),
            request.Page, request.PageSize, total);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct) => await DtoAsync(await FindAsync(id, includeDeleted: true, ct), ct);

    public async Task<UserDto> CreateAsync(UserCreateInput input, CancellationToken ct)
    {
        var roles = await LoadRolesAsync(input.RoleIds ?? [], ct);
        if (roles.Any(IsSuperAdminRole)) await RequireSuperAdminActorAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = new User { Email = input.Email.Trim(), DisplayName = input.DisplayName.Trim(), MustChangePassword = true };
        var result = await users.CreateAsync(user, input.TemporaryPassword);
        ThrowIfFailed(result, nameof(input.TemporaryPassword));
        foreach (var role in roles) db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await SaveAsync(db, EmailTaken, ct);
        await transaction.CommitAsync(ct);
        return await DtoAsync(user, ct);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UserUpdateInput input, CancellationToken ct)
    {
        var user = await FindAsync(id, includeDeleted: false, ct);
        var targetIsSuper = await IsSuperAdminAsync(user.Id, ct);
        if (targetIsSuper) await RequireSuperAdminActorAsync(ct);
        ExpectVersion(db, user, input.Version);
        var deactivating = user.IsActive && !input.IsActive;
        if (deactivating && user.Id == actor.UserId) throw new ConflictException("You cannot deactivate your own account.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (deactivating && targetIsSuper) await EnsureAnotherSuperAdminAsync(user.Id, ct);
        user.Email = input.Email.Trim();
        user.NormalizedEmail = users.NormalizeEmail(user.Email) ?? "";
        user.DisplayName = input.DisplayName.Trim();
        user.IsActive = input.IsActive;
        if (deactivating) await EndSessionsAsync(user, "deactivated", ct);
        await SaveAsync(db, EmailTaken, ct);
        await transaction.CommitAsync(ct);
        access.Invalidate(user.Id);
        return await DtoAsync(user, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var user = await FindAsync(id, includeDeleted: false, ct);
        if (user.Id == actor.UserId) throw new ConflictException("You cannot delete your own account.");
        var targetIsSuper = await IsSuperAdminAsync(user.Id, ct);
        if (targetIsSuper) await RequireSuperAdminActorAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (targetIsSuper && user.IsActive) await EnsureAnotherSuperAdminAsync(user.Id, ct);
        await EndSessionsAsync(user, "deleted", ct);
        db.Users.Remove(user); // soft delete
        await SaveAsync(db, EmailTaken, ct);
        await transaction.CommitAsync(ct);
        access.Invalidate(user.Id);
    }

    public async Task<UserDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        var user = await FindAsync(id, includeDeleted: true, ct);
        if (!user.IsDeleted) return await DtoAsync(user, ct);
        if (await IsSuperAdminAsync(user.Id, ct)) await RequireSuperAdminActorAsync(ct);
        user.IsDeleted = false;
        user.DeletedAt = null;
        user.DeletedBy = null;
        await SaveAsync(db, "Another user now has this email address. Change that user's email first.", ct);
        access.Invalidate(user.Id);
        return await DtoAsync(user, ct);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordInput input, CancellationToken ct)
    {
        var user = await FindAsync(id, includeDeleted: false, ct);
        if (await IsSuperAdminAsync(user.Id, ct)) await RequireSuperAdminActorAsync(ct);
        if (string.Equals(input.TemporaryPassword, user.Email, StringComparison.OrdinalIgnoreCase))
            throw Invalid(nameof(input.TemporaryPassword), "Password cannot be the email address.");
        foreach (var validator in users.PasswordValidators)
            ThrowIfFailed(await validator.ValidateAsync(users, user, input.TemporaryPassword), nameof(input.TemporaryPassword));
        user.PasswordHash = users.PasswordHasher.HashPassword(user, input.TemporaryPassword);
        user.MustChangePassword = true;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        await EndSessionsAsync(user, "password-reset", ct);
        SecurityEvents.Add(db, clock.GetUtcNow(), SecurityEvents.PasswordReset, user.Id, null, $"by {actor.ActorId}");
        await SaveAsync(db, EmailTaken, ct);
        access.Invalidate(user.Id);
    }

    public async Task<UserDto> UnlockAsync(Guid id, CancellationToken ct)
    {
        var user = await FindAsync(id, includeDeleted: false, ct);
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        await SaveAsync(db, EmailTaken, ct);
        return await DtoAsync(user, ct);
    }

    public async Task<UserDto> SetRolesAsync(Guid id, UserRolesInput input, CancellationToken ct)
    {
        var user = await FindAsync(id, includeDeleted: false, ct);
        var wanted = await LoadRolesAsync(input.RoleIds, ct);
        var current = await db.UserRoles.Include(x => x.Role).Where(x => x.UserId == user.Id).ToListAsync(ct);
        var wasSuper = current.Any(x => IsSuperAdminRole(x.Role!));
        var willBeSuper = wanted.Any(IsSuperAdminRole);
        if (wasSuper || willBeSuper) await RequireSuperAdminActorAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (wasSuper && !willBeSuper)
        {
            if (user.IsActive) await EnsureAnotherSuperAdminAsync(user.Id, ct);
            await EndSessionsAsync(user, "superadmin-removed", ct);
        }
        foreach (var row in current.Where(r => wanted.All(w => w.Id != r.RoleId))) db.UserRoles.Remove(row);
        foreach (var role in wanted.Where(w => current.All(r => r.RoleId != w.Id))) db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await SaveAsync(db, "The roles were changed by someone else. Reload and try again.", ct);
        await transaction.CommitAsync(ct);
        access.Invalidate(user.Id);
        return await DtoAsync(user, ct);
    }

    public async Task<UserPermissionsDto> GetPermissionsAsync(Guid id, CancellationToken ct)
    {
        var user = await FindAsync(id, includeDeleted: false, ct);
        var isSuper = await IsSuperAdminAsync(user.Id, ct);
        var fromRoles = (await db.RolePermissions.Where(p => db.UserRoles.Any(r => r.UserId == user.Id && r.RoleId == p.RoleId))
            .Select(p => p.PermissionKey).Distinct().ToListAsync(ct)).ToHashSet();
        var overrides = await db.UserPermissionOverrides.Where(x => x.UserId == user.Id).ToDictionaryAsync(x => x.PermissionKey, x => x.Effect, ct);
        var effective = AccessService.Evaluate(isSuper, fromRoles, overrides.Select(x => (x.Key, x.Value)));
        var states = PermissionKeys.All.Select(p => new UserPermissionState(p.Key, p.Group, p.Description, isSuper || fromRoles.Contains(p.Key),
            overrides.TryGetValue(p.Key, out var effect) ? (OverrideState)(int)effect : OverrideState.Inherit, effective.Contains(p.Key))).ToList();
        var menus = await db.Menus.AsNoTracking().Where(x => x.IsEnabled).ToListAsync(ct);
        var tree = MenuTree.Build<UserPermissionMenu>(menus, null, _ => true, (m, children) => new UserPermissionMenu(m.Key, m.Title, m.RequiredPermission, children));
        return new UserPermissionsDto(user.Id, isSuper, tree, states);
    }

    public async Task<UserPermissionsDto> SetPermissionsAsync(Guid id, UserPermissionsInput input, CancellationToken ct)
    {
        var user = await FindAsync(id, includeDeleted: false, ct);
        if (await IsSuperAdminAsync(user.Id, ct)) throw new ConflictException("A SuperAdmin always has every permission, so overrides do not apply.");
        var wanted = input.Overrides.Where(x => x.State != OverrideState.Inherit).ToDictionary(x => x.Key, x => (PermissionEffect)(int)x.State);
        var current = await db.UserPermissionOverrides.Where(x => x.UserId == user.Id).ToListAsync(ct);
        foreach (var row in current)
        {
            if (!wanted.TryGetValue(row.PermissionKey, out var effect)) db.UserPermissionOverrides.Remove(row);
            else row.Effect = effect;
        }
        foreach (var (key, effect) in wanted.Where(w => current.All(c => c.PermissionKey != w.Key)))
            db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = user.Id, PermissionKey = key, Effect = effect });
        await SaveAsync(db, "The permissions were changed by someone else. Reload and try again.", ct);
        access.Invalidate(user.Id);
        return await GetPermissionsAsync(user.Id, ct);
    }

    private async Task<User> FindAsync(Guid id, bool includeDeleted, CancellationToken ct) =>
        await (includeDeleted ? db.Users.IgnoreQueryFilters() : db.Users).SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new NotFoundException("User not found.");

    private async Task<List<Role>> LoadRolesAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var roles = await db.Roles.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (roles.Count != ids.Distinct().Count()) throw Invalid("RoleIds", "One or more roles do not exist.");
        return roles;
    }

    private static bool IsSuperAdminRole(Role role) => role.NormalizedName == Role.SuperAdmin.ToUpperInvariant();

    private Task<bool> IsSuperAdminAsync(Guid userId, CancellationToken ct) =>
        db.UserRoles.AnyAsync(x => x.UserId == userId && x.Role!.NormalizedName == Role.SuperAdmin.ToUpperInvariant(), ct);

    private async Task RequireSuperAdminActorAsync(CancellationToken ct)
    {
        if (actor.UserId is not { } me || !(await access.GetAsync(me, ct)).IsSuperAdmin)
            throw new ForbiddenException("Only a SuperAdmin can change SuperAdmin accounts or the SuperAdmin role.");
    }

    /// <summary>Call inside a transaction before an active SuperAdmin stops being one.</summary>
    private async Task EnsureAnotherSuperAdminAsync(Guid leavingUserId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SuperAdminLock})", ct);
        var others = await db.UserRoles.CountAsync(r => r.UserId != leavingUserId && r.Role!.NormalizedName == Role.SuperAdmin.ToUpperInvariant()
            && db.Users.Any(u => u.Id == r.UserId && u.IsActive), ct);
        if (others == 0) throw new ConflictException("This is the last active SuperAdmin. Make another user SuperAdmin first.");
    }

    private async Task EndSessionsAsync(User user, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await RefreshTokens.RevokeUserAsync(db, user.Id, reason, actor.ActorId, now, ct);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        SecurityEvents.Add(db, now, SecurityEvents.SessionsRevoked, user.Id, null, reason);
    }

    private static void ThrowIfFailed(IdentityResult result, string property)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
            throw new ConflictException(EmailTaken);
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            throw new ConflictException("This record was changed by someone else. Reload it and try again.");
        throw IdentityErrors.ToValidation(result, property);
    }

    private async Task<UserDto> DtoAsync(User user, CancellationToken ct)
    {
        var roles = await db.UserRoles.AsNoTracking().Where(x => x.UserId == user.Id).OrderBy(x => x.Role!.Name)
            .Select(x => new RoleRef(x.RoleId, x.Role!.Name)).ToListAsync(ct);
        return new UserDto(user.Id, user.Email, user.DisplayName, user.IsActive, user.LockoutEnd > clock.GetUtcNow(), user.IsDeleted, user.MustChangePassword,
            roles, user.LastLoginAt, user.UpdatedAt, user.Version);
    }
}
