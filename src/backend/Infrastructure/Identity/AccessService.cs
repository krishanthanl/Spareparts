using System.Security.Cryptography;
using System.Text;
using Application.Identity;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Identity;

/// <summary>
/// Works out what a user may do. Results are cached for <see cref="CacheFor"/> so permission changes
/// reach every server within that time; changes on this server clear the cache at once.
/// </summary>
public sealed class AccessService(AppDbContext db, IMemoryCache cache) : IAccessService
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);
    private static int generation;

    private static string Key(Guid userId) => $"access:{Volatile.Read(ref generation)}:{userId:N}";

    public async Task<AccessSnapshot> GetAsync(Guid userId, CancellationToken ct) =>
        (await cache.GetOrCreateAsync(Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return await LoadAsync(userId, ct);
        }))!;

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    public void InvalidateAll() => Interlocked.Increment(ref generation);

    private async Task<AccessSnapshot> LoadAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.IsActive, x.SecurityStamp }).SingleOrDefaultAsync(ct);
        if (user is null) return new AccessSnapshot(userId, false, "", false, new HashSet<string>());
        var roles = await db.UserRoles.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Role!.NormalizedName).ToListAsync(ct);
        var rolePermissions = await db.RolePermissions.AsNoTracking()
            .Where(p => db.UserRoles.Any(r => r.UserId == userId && r.RoleId == p.RoleId)).Select(p => p.PermissionKey).Distinct().ToListAsync(ct);
        var overrides = await db.UserPermissionOverrides.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.PermissionKey, x.Effect }).ToListAsync(ct);
        var isSuperAdmin = roles.Contains(Role.SuperAdmin.ToUpperInvariant());
        var permissions = Evaluate(isSuperAdmin, rolePermissions, overrides.Select(x => (x.PermissionKey, x.Effect)));
        return new AccessSnapshot(userId, user.IsActive, StampHash(user.SecurityStamp), isSuperAdmin, permissions);
    }

    /// <summary>
    /// Effective permissions: role permissions plus user Allows, minus user Denies. Deny wins.
    /// SuperAdmin has every permission and cannot be denied. Unknown keys are ignored.
    /// </summary>
    public static IReadOnlySet<string> Evaluate(bool isSuperAdmin, IEnumerable<string> rolePermissions, IEnumerable<(string Key, PermissionEffect Effect)> overrides)
    {
        if (isSuperAdmin) return PermissionKeys.All.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var list = overrides.ToList();
        var result = rolePermissions.Where(PermissionKeys.IsKnown).ToHashSet(StringComparer.Ordinal);
        result.UnionWith(list.Where(x => x.Effect == PermissionEffect.Allow && PermissionKeys.IsKnown(x.Key)).Select(x => x.Key));
        result.ExceptWith(list.Where(x => x.Effect == PermissionEffect.Deny).Select(x => x.Key));
        return result;
    }

    /// <summary>Short fingerprint of the security stamp, carried in access tokens.</summary>
    public static string StampHash(string stamp) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..16];
}
