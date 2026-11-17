using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Application.Identity;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Identity;

/// <summary>Session and lockout limits (ADR 0003). Bound from the "Auth" configuration section.</summary>
public sealed class SessionSettings
{
    public int RefreshTokenDays { get; set; } = 14;
    public int SessionMaxDays { get; set; } = 30;
    public int RefreshReuseGraceSeconds { get; set; } = 5;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int LoginAttemptsPerEmailPerMinute { get; set; } = 5;
}

internal static class RefreshTokens
{
    public static string NewRaw() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    public static Task<int> RevokeFamilyAsync(AppDbContext db, Guid familyId, string reason, string actor, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens.Where(x => x.FamilyId == familyId && x.RevokedAt == null).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason)
            .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, actor), ct);

    public static Task<int> RevokeUserAsync(AppDbContext db, Guid userId, string reason, string actor, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason)
            .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, actor), ct);
}

internal static class SecurityEvents
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LoginBlocked = "LoginBlocked";
    public const string LockedOut = "LockedOut";
    public const string TokenReplay = "TokenReplay";
    public const string Logout = "Logout";
    public const string LogoutAll = "LogoutAll";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string SessionsRevoked = "SessionsRevoked";

    public static void Add(AppDbContext db, DateTimeOffset now, string type, Guid? userId, string? ip, string? details = null) =>
        db.SecurityEvents.Add(new SecurityEvent { Type = type, UserId = userId, OccurredAt = now, Ip = Trim(ip, 45), Details = Trim(details, 500) });

    public static string? Trim(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

internal static class MenuTree
{
    public static List<T> Build<T>(IReadOnlyList<Menu> menus, Guid? parent, Func<Menu, bool> visible, Func<Menu, List<T>, T> map)
    {
        var result = new List<T>();
        foreach (var menu in menus.Where(x => x.ParentId == parent).OrderBy(x => x.SortOrder))
        {
            var children = Build(menus, menu.Id, visible, map);
            // A group (no path) shows only when something inside it is visible.
            var show = menu.Path is null ? children.Count > 0 && (menu.RequiredPermission is null || visible(menu)) : visible(menu);
            if (show) result.Add(map(menu, children));
        }
        return result;
    }

    public static IReadOnlyList<MenuNode> ForUser(IReadOnlyList<Menu> menus, AccessSnapshot access) =>
        Build<MenuNode>(menus, null, m => m.RequiredPermission is null || access.Has(m.RequiredPermission),
            (m, children) => new MenuNode(m.Key, m.Title, m.Path, m.Icon, children));
}
