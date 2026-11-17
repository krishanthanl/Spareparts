using System.Runtime.CompilerServices;
using Application.Common;
using Application.Identity;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using static Infrastructure.Catalog.CatalogPersistence;

namespace Infrastructure.Identity;

public sealed class AuthService(
    AppDbContext db, UserManager<User> users, IAccessService access, IAccessTokenIssuer issuer,
    TimeProvider clock, SessionSettings settings, IMemoryCache cache, ICurrentActor actor) : IAuthService
{
    // Verifying against a throwaway hash keeps unknown-email logins as slow as real ones.
    private static string? dummyHash;

    public async Task<AuthSession> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var normalized = users.NormalizeEmail(request.Email.Trim()) ?? "";
        ThrottleEmail(normalized);

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || user.UserType != UserType.Staff || user.PasswordHash is null)
        {
            dummyHash ??= users.PasswordHasher.HashPassword(new User(), Guid.NewGuid().ToString());
            users.PasswordHasher.VerifyHashedPassword(new User(), dummyHash, request.Password);
            await FailAsync(SecurityEvents.LoginFailed, user?.Id, client, user is null ? "unknown email" : "inactive", ct);
        }
        if (await users.IsLockedOutAsync(user!))
            await FailAsync(SecurityEvents.LoginBlocked, user!.Id, client, "locked out", ct);
        if (!await users.CheckPasswordAsync(user!, request.Password))
        {
            await users.AccessFailedAsync(user!);
            var locked = await users.IsLockedOutAsync(user!);
            await FailAsync(locked ? SecurityEvents.LockedOut : SecurityEvents.LoginFailed, user!.Id, client, "wrong password", ct);
        }

        await users.ResetAccessFailedCountAsync(user!);
        user!.LastLoginAt = now;
        SecurityEvents.Add(db, now, SecurityEvents.LoginSucceeded, user.Id, client.Ip);
        return await StartSessionAsync(user, client, ct);
    }

    public async Task<AuthSession> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 100) throw new AuthenticationFailedException(AuthenticationFailedException.SessionEnded);
        var now = clock.GetUtcNow();
        var token = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == RefreshTokens.Hash(refreshToken), ct);
        if (token is null || token.RevokedAt is not null) throw new AuthenticationFailedException(AuthenticationFailedException.SessionEnded);
        if (token.ConsumedAt is { } consumed)
        {
            if (now - consumed <= TimeSpan.FromSeconds(settings.RefreshReuseGraceSeconds)) throw RetryRefresh();
            // An old token came back: someone may have copied it. End the whole sign-in.
            await RefreshTokens.RevokeFamilyAsync(db, token.FamilyId, "replay", token.UserId.ToString(), now, ct);
            SecurityEvents.Add(db, now, SecurityEvents.TokenReplay, token.UserId, client.Ip, $"family {token.FamilyId}");
            await db.SaveChangesAsync(ct);
            access.Invalidate(token.UserId);
            throw new AuthenticationFailedException(AuthenticationFailedException.SessionEnded);
        }
        if (token.ExpiresAt <= now || token.FamilyExpiresAt <= now) throw new AuthenticationFailedException(AuthenticationFailedException.SessionEnded);

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == token.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await RefreshTokens.RevokeFamilyAsync(db, token.FamilyId, "inactive", token.UserId.ToString(), now, ct);
            throw new AuthenticationFailedException(AuthenticationFailedException.SessionEnded);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var next = new RefreshToken { FamilyId = token.FamilyId, FamilyExpiresAt = token.FamilyExpiresAt };
        // Consume atomically: of two requests with the same token only one can win this update.
        var won = await db.RefreshTokens.Where(x => x.Id == token.Id && x.ConsumedAt == null && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now).SetProperty(x => x.ReplacedById, next.Id)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, token.UserId.ToString()), ct);
        if (won == 0) throw RetryRefresh();
        var session = await IssueAsync(user, next, client, ct);
        await transaction.CommitAsync(ct);
        return session;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 100) return;
        var token = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == RefreshTokens.Hash(refreshToken), ct);
        if (token is null) return;
        var now = clock.GetUtcNow();
        await RefreshTokens.RevokeFamilyAsync(db, token.FamilyId, "logout", token.UserId.ToString(), now, ct);
        SecurityEvents.Add(db, now, SecurityEvents.Logout, token.UserId, null);
        await db.SaveChangesAsync(ct);
    }

    public async Task LogoutAllAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct) ?? throw new AuthenticationFailedException(AuthenticationFailedException.SessionEnded);
        var now = clock.GetUtcNow();
        await RefreshTokens.RevokeUserAsync(db, userId, "logout-all", actor.ActorId, now, ct);
        user.SecurityStamp = Guid.NewGuid().ToString("N"); // ends access tokens too
        SecurityEvents.Add(db, now, SecurityEvents.LogoutAll, userId, null);
        await db.SaveChangesAsync(ct);
        access.Invalidate(userId);
    }

    public async Task<AuthSession> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, ClientInfo client, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct) ?? throw new AuthenticationFailedException(AuthenticationFailedException.SessionEnded);
        if (string.Equals(request.NewPassword, user.Email, StringComparison.OrdinalIgnoreCase))
            throw Invalid(nameof(request.NewPassword), "Password cannot be the email address.");
        if (await users.IsLockedOutAsync(user)) throw new ForbiddenException("This account is locked. Try again later.");
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                await users.AccessFailedAsync(user);
                throw Invalid(nameof(request.CurrentPassword), "The current password is incorrect.");
            }
            throw IdentityErrors.ToValidation(result, nameof(request.NewPassword));
        }
        var now = clock.GetUtcNow();
        user.MustChangePassword = false;
        await RefreshTokens.RevokeUserAsync(db, userId, "password-changed", userId.ToString(), now, ct);
        SecurityEvents.Add(db, now, SecurityEvents.PasswordChanged, userId, client.Ip);
        access.Invalidate(userId);
        // Every other session has ended; this one continues with a fresh sign-in.
        return await StartSessionAsync(user, client, ct);
    }

    public async Task<SessionUser> GetSessionUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new AuthenticationFailedException(AuthenticationFailedException.SessionEnded);
        return await ToSessionUserAsync(user, ct);
    }

    public async Task<IReadOnlyList<MenuNode>> GetMenusAsync(Guid userId, CancellationToken ct)
    {
        var snapshot = await access.GetAsync(userId, ct);
        var menus = await db.Menus.AsNoTracking().Where(x => x.IsEnabled).ToListAsync(ct);
        return MenuTree.ForUser(menus, snapshot);
    }

    private async Task<AuthSession> StartSessionAsync(User user, ClientInfo client, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var first = new RefreshToken { FamilyId = Guid.NewGuid(), FamilyExpiresAt = now.AddDays(settings.SessionMaxDays) };
        return await IssueAsync(user, first, client, ct);
    }

    private async Task<AuthSession> IssueAsync(User user, RefreshToken token, ClientInfo client, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var raw = RefreshTokens.NewRaw();
        token.UserId = user.Id;
        token.TokenHash = RefreshTokens.Hash(raw);
        var sliding = now.AddDays(settings.RefreshTokenDays);
        token.ExpiresAt = sliding < token.FamilyExpiresAt ? sliding : token.FamilyExpiresAt;
        token.CreatedIp = SecurityEvents.Trim(client.Ip, 45);
        token.UserAgent = SecurityEvents.Trim(client.UserAgent, 200);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
        var accessToken = issuer.Issue(new AccessTokenRequest(user.Id, token.FamilyId, AccessService.StampHash(user.SecurityStamp)));
        return new AuthSession(new AuthResponse(accessToken.Token, accessToken.ExpiresAt, await ToSessionUserAsync(user, ct)), raw, token.ExpiresAt);
    }

    private async Task<SessionUser> ToSessionUserAsync(User user, CancellationToken ct)
    {
        var snapshot = await access.GetAsync(user.Id, ct);
        var roles = await db.UserRoles.AsNoTracking().Where(x => x.UserId == user.Id).Select(x => x.Role!.Name).OrderBy(x => x).ToListAsync(ct);
        return new SessionUser(user.Id, user.Email, user.DisplayName, user.MustChangePassword, roles, snapshot.Permissions.Order(StringComparer.Ordinal).ToList());
    }

    private void ThrottleEmail(string normalizedEmail)
    {
        var counter = cache.GetOrCreate("login-email:" + normalizedEmail, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return new StrongBox<int>();
        })!;
        if (Interlocked.Increment(ref counter.Value) > settings.LoginAttemptsPerEmailPerMinute) throw new RateLimitedException();
    }

    private async Task FailAsync(string type, Guid? userId, ClientInfo client, string details, CancellationToken ct)
    {
        SecurityEvents.Add(db, clock.GetUtcNow(), type, userId, client.Ip, details);
        await db.SaveChangesAsync(ct);
        // Same answer whatever the reason, so the response never reveals which accounts exist.
        throw new AuthenticationFailedException();
    }

    private static ConflictException RetryRefresh() => new("The session is already being refreshed. Try again.");
}

internal static class IdentityErrors
{
    public static FluentValidation.ValidationException ToValidation(IdentityResult result, string property) =>
        new(result.Errors.Select(e => new FluentValidation.Results.ValidationFailure(property, e.Description)));
}
