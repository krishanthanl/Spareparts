using Application.Common;
using Application.Identity;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Identity;

public sealed class RoleService(AppDbContext db, IAccessService access) : IRoleService
{
    private const string NameTaken = "A role with this name already exists.";

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().Include(x => x.Permissions).OrderBy(x => !x.IsSystem).ThenBy(x => x.Name).ToListAsync(ct);
        var counts = await db.UserRoles.Where(r => db.Users.Any(u => u.Id == r.UserId))
            .GroupBy(x => x.RoleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<RoleDto> CreateAsync(RoleInput input, CancellationToken ct)
    {
        var role = new Role { Name = input.Name.Trim(), NormalizedName = input.Name.Trim().ToUpperInvariant(), Description = input.Description?.Trim() };
        db.Roles.Add(role);
        foreach (var key in input.Permissions) db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = key });
        await SaveAsync(db, NameTaken, ct);
        return ToDto(await ReloadAsync(role.Id, ct), 0);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, RoleInput input, CancellationToken ct)
    {
        var role = await db.Roles.Include(x => x.Permissions).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Role not found.");
        ExpectVersion(db, role, input.Version);
        var name = input.Name.Trim();
        if (role.IsSystem && name != role.Name) throw new ConflictException("System roles cannot be renamed.");
        var wanted = input.Permissions.ToHashSet();
        if (role.NormalizedName == Role.SuperAdmin.ToUpperInvariant() && wanted.Count > 0)
            throw new ConflictException("SuperAdmin always has every permission; its permissions cannot be edited.");
        role.Name = name;
        role.NormalizedName = name.ToUpperInvariant();
        role.Description = input.Description?.Trim();
        foreach (var row in role.Permissions.Where(p => !wanted.Contains(p.PermissionKey)).ToList()) db.RolePermissions.Remove(row);
        foreach (var key in wanted.Where(k => role.Permissions.All(p => p.PermissionKey != k)))
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = key });
        // Touch the role so its version changes even when only permissions changed.
        db.Entry(role).State = EntityState.Modified;
        await SaveAsync(db, NameTaken, ct);
        access.InvalidateAll();
        var count = await db.UserRoles.CountAsync(r => r.RoleId == id && db.Users.Any(u => u.Id == r.UserId), ct);
        return ToDto(await ReloadAsync(id, ct), count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Role not found.");
        if (role.IsSystem) throw new ConflictException("System roles cannot be deleted.");
        if (await db.UserRoles.AnyAsync(x => x.RoleId == id, ct)) throw new ConflictException("Remove this role from its users before deleting it.");
        db.Roles.Remove(role);
        await SaveAsync(db, NameTaken, ct);
        access.InvalidateAll();
    }

    private async Task<Role> ReloadAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return await db.Roles.AsNoTracking().Include(x => x.Permissions).SingleAsync(x => x.Id == id, ct);
    }

    private static RoleDto ToDto(Role role, int userCount) => new(role.Id, role.Name, role.Description, role.IsSystem,
        role.NormalizedName == Role.SuperAdmin.ToUpperInvariant(), userCount,
        role.Permissions.Select(p => p.PermissionKey).Order(StringComparer.Ordinal).ToList(), role.Version);
}
