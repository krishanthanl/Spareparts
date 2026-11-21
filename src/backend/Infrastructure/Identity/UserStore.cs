using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity store over our own <see cref="User"/> entity. Identity provides password
/// hashing, lockout and security stamps; the email address is the user name.
/// </summary>
public sealed class UserStore(AppDbContext db, IdentityErrorDescriber errors) :
    IUserPasswordStore<User>, IUserSecurityStampStore<User>, IUserEmailStore<User>, IUserLockoutStore<User>
{
    public Task<string> GetUserIdAsync(User user, CancellationToken ct) => Task.FromResult(user.Id.ToString());
    public Task<string?> GetUserNameAsync(User user, CancellationToken ct) => Task.FromResult<string?>(user.Email);
    public Task SetUserNameAsync(User user, string? userName, CancellationToken ct) { user.Email = userName ?? ""; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken ct) => Task.FromResult<string?>(user.NormalizedEmail);
    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken ct) { user.NormalizedEmail = normalizedName ?? ""; return Task.CompletedTask; }

    public async Task<IdentityResult> CreateAsync(User user, CancellationToken ct)
    {
        db.Users.Add(user);
        return await SaveAsync(ct);
    }

    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken ct)
    {
        if (db.Entry(user).State == EntityState.Detached) db.Users.Update(user);
        return await SaveAsync(ct);
    }

    public async Task<IdentityResult> DeleteAsync(User user, CancellationToken ct)
    {
        db.Users.Remove(user); // soft delete through the audit interceptor
        return await SaveAsync(ct);
    }

    private async Task<IdentityResult> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return IdentityResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return IdentityResult.Failed(errors.ConcurrencyFailure());
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return IdentityResult.Failed(errors.DuplicateEmail(""));
        }
    }

    public Task<User?> FindByIdAsync(string userId, CancellationToken ct) =>
        Guid.TryParse(userId, out var id) ? db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) : Task.FromResult<User?>(null);

    public Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken ct) => FindByEmailAsync(normalizedUserName, ct);

    public Task SetPasswordHashAsync(User user, string? passwordHash, CancellationToken ct) { user.PasswordHash = passwordHash; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(User user, CancellationToken ct) => Task.FromResult(user.PasswordHash);
    public Task<bool> HasPasswordAsync(User user, CancellationToken ct) => Task.FromResult(user.PasswordHash is not null);

    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken ct) { user.SecurityStamp = stamp; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(User user, CancellationToken ct) => Task.FromResult<string?>(user.SecurityStamp);

    public Task SetEmailAsync(User user, string? email, CancellationToken ct) { user.Email = email ?? ""; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(User user, CancellationToken ct) => Task.FromResult<string?>(user.Email);
    // Staff accounts are created by an administrator, so the address is treated as confirmed.
    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken ct) => Task.FromResult(true);
    public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken ct) => Task.CompletedTask;
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) => db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, ct);
    public Task<string?> GetNormalizedEmailAsync(User user, CancellationToken ct) => Task.FromResult<string?>(user.NormalizedEmail);
    public Task SetNormalizedEmailAsync(User user, string? normalizedEmail, CancellationToken ct) { user.NormalizedEmail = normalizedEmail ?? ""; return Task.CompletedTask; }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(User user, CancellationToken ct) => Task.FromResult(user.LockoutEnd);
    public Task SetLockoutEndDateAsync(User user, DateTimeOffset? lockoutEnd, CancellationToken ct) { user.LockoutEnd = lockoutEnd?.ToUniversalTime(); return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(User user, CancellationToken ct) => Task.FromResult(++user.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(User user, CancellationToken ct) { user.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(User user, CancellationToken ct) => Task.FromResult(user.AccessFailedCount);
    // Lockout always applies; there is no per-user opt-out.
    public Task<bool> GetLockoutEnabledAsync(User user, CancellationToken ct) => Task.FromResult(true);
    public Task SetLockoutEnabledAsync(User user, bool enabled, CancellationToken ct) => Task.CompletedTask;

    public void Dispose() { }
}
