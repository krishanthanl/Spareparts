using Application.Common;
using Application.Identity;
using Domain.Identity;
using Infrastructure;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Backend.IntegrationTests;

/// <summary>
/// Sign-in, refresh rotation, replay detection and SuperAdmin protection against a real, throwaway
/// PostgreSQL database with the real migrations applied.
/// </summary>
public sealed class IdentityPostgresTests : IAsyncLifetime
{
    private const string Password = "correct-horse-battery";
    private static readonly ClientInfo Client = new("127.0.0.1", "tests");
    private PostgreSqlContainer? container;
    private string adminConnection = "";
    private string connection = "";
    private string database = "";
    private ServiceProvider? provider;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("RUN_POSTGRES_TESTS") != "1" && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION"))) return;
        adminConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION") ?? "";
        if (string.IsNullOrWhiteSpace(adminConnection))
        {
            container = new PostgreSqlBuilder("postgres:17").Build();
            await container.StartAsync();
            adminConnection = container.GetConnectionString();
        }
        database = "identity_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database }.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TestActor>();
        services.AddScoped<ICurrentActor>(sp => sp.GetRequiredService<TestActor>());
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o.UseNpgsql(connection).AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
        services.AddIdentityInfrastructure(new SessionSettings());
        services.AddSingleton<IAccessTokenIssuer, StubIssuer>();
        provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(default);
    }

    public async Task DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (database.Length > 0)
        {
            await using var admin = new NpgsqlConnection(adminConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
        if (container is not null) await container.DisposeAsync();
    }

    /// <summary>A fresh scope, acting as the given user (or anonymous).</summary>
    private AsyncServiceScope Scope(Guid? actor = null)
    {
        var scope = provider!.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestActor>().ActorId = actor?.ToString() ?? "anonymous";
        return scope;
    }

    private async Task<Guid> CreateUserAsync(string email, string? role = null, bool active = true)
    {
        await using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Email = email, DisplayName = email, IsActive = active };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        if (role is not null)
        {
            var roleId = await db.Roles.Where(x => x.Name == role).Select(x => x.Id).SingleAsync();
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
            await db.SaveChangesAsync();
        }
        return user.Id;
    }

    private async Task<AuthSession> LoginAsync(string email, string password = Password)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<IAuthService>().LoginAsync(new LoginRequest(email, password), Client, default);
    }

    private async Task<AuthSession> RefreshAsync(string token)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<IAuthService>().RefreshAsync(token, Client, default);
    }

    [PostgresFact]
    public async Task Login_returns_permissions_and_refresh_rotates_the_token()
    {
        await CreateUserAsync("keeper@shop.nz", Role.StoreKeeper);
        var session = await LoginAsync("KEEPER@shop.nz");
        Assert.Contains(PermissionKeys.CatalogManage, session.Response.User.Permissions);
        Assert.DoesNotContain(PermissionKeys.UsersManage, session.Response.User.Permissions);
        var next = await RefreshAsync(session.RefreshToken);
        Assert.NotEqual(session.RefreshToken, next.RefreshToken);

        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = await db.RefreshTokens.OrderBy(x => x.CreatedAt).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.Equal(tokens[1].Id, tokens[0].ReplacedById);
        Assert.NotNull(tokens[0].ConsumedAt);
        Assert.All(tokens, t => Assert.Equal(64, t.TokenHash.Length));
        Assert.DoesNotContain(tokens, t => t.TokenHash == session.RefreshToken); // only hashes are stored
    }

    [PostgresFact]
    public async Task Wrong_password_unknown_email_and_inactive_user_get_the_same_answer()
    {
        await CreateUserAsync("idle@shop.nz", Role.Cashier, active: false);
        await CreateUserAsync("real@shop.nz", Role.Cashier);
        var messages = new List<string>();
        foreach (var (email, password) in new[] { ("real@shop.nz", "wrong-password-1"), ("nobody@shop.nz", Password), ("idle@shop.nz", Password) })
            messages.Add((await Assert.ThrowsAsync<AuthenticationFailedException>(() => LoginAsync(email, password))).Message);
        Assert.Single(messages.Distinct());
    }

    [PostgresFact]
    public async Task Five_failures_lock_the_account_even_with_the_right_password()
    {
        await CreateUserAsync("lock@shop.nz", Role.Cashier);
        for (var i = 0; i < 5; i++)
        {
            // Spread over time windows is not needed: the per-email limit is 5 per minute, the lockout is the 5th failure.
            await Assert.ThrowsAsync<AuthenticationFailedException>(() => LoginAsync("lock@shop.nz", "wrong-password-" + i));
        }
        await using var scope = Scope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(x => x.Email == "lock@shop.nz");
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [PostgresFact]
    public async Task Simultaneous_refreshes_with_one_token_have_exactly_one_winner()
    {
        await CreateUserAsync("twin@shop.nz", Role.Cashier);
        var session = await LoginAsync("twin@shop.nz");
        var attempts = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await RefreshAsync(session.RefreshToken); return true; }
            catch (ConflictException) { return false; }
        })).ToList();
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(x => x));
    }

    [PostgresFact]
    public async Task Reusing_a_consumed_token_after_the_grace_window_revokes_the_whole_sign_in()
    {
        await CreateUserAsync("replay@shop.nz", Role.Cashier);
        var first = await LoginAsync("replay@shop.nz");
        var second = await RefreshAsync(first.RefreshToken);
        await using (var scope = Scope())
        {
            // Pretend the first token was used long ago.
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.Where(x => x.ConsumedAt != null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(first.RefreshToken));
        // The legitimate newer token is dead too.
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(second.RefreshToken));
        await using var check = Scope();
        Assert.True(await check.ServiceProvider.GetRequiredService<AppDbContext>().SecurityEvents.AnyAsync(x => x.Type == "TokenReplay"));
    }

    [PostgresFact]
    public async Task Logout_ends_the_sign_in()
    {
        await CreateUserAsync("bye@shop.nz", Role.Cashier);
        var session = await LoginAsync("bye@shop.nz");
        await using (var scope = Scope()) await scope.ServiceProvider.GetRequiredService<IAuthService>().LogoutAsync(session.RefreshToken, default);
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(session.RefreshToken));
    }

    [PostgresFact]
    public async Task Deactivated_user_cannot_refresh_and_password_change_ends_other_sessions()
    {
        var admin = await CreateUserAsync("boss@shop.nz", Role.SuperAdmin);
        var staff = await CreateUserAsync("staff@shop.nz", Role.Cashier);
        var laptop = await LoginAsync("staff@shop.nz");
        var phone = await LoginAsync("staff@shop.nz");

        await using (var scope = Scope(staff))
        {
            var changed = await scope.ServiceProvider.GetRequiredService<IAuthService>()
                .ChangePasswordAsync(staff, new ChangePasswordRequest(Password, "a-brand-new-password"), Client, default);
            Assert.NotEmpty(changed.RefreshToken);
        }
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(laptop.RefreshToken));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(phone.RefreshToken));

        var again = await LoginAsync("staff@shop.nz", "a-brand-new-password");
        await using (var scope = Scope(admin))
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
            var current = await users.GetAsync(staff, default);
            await users.UpdateAsync(staff, new UserUpdateInput(current.Email, current.DisplayName, false, current.Version), default);
        }
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(again.RefreshToken));
    }

    [PostgresFact]
    public async Task Two_admins_cannot_remove_the_last_two_SuperAdmins_at_once()
    {
        var a = await CreateUserAsync("a@shop.nz", Role.SuperAdmin);
        var b = await CreateUserAsync("b@shop.nz", Role.SuperAdmin);
        async Task<bool> Demote(Guid actor, Guid target)
        {
            await using var scope = Scope(actor);
            try
            {
                await scope.ServiceProvider.GetRequiredService<IUserAdminService>().SetRolesAsync(target, new UserRolesInput([]), default);
                return true;
            }
            catch (ConflictException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Demote(a, b)), Task.Run(() => Demote(b, a)));
        Assert.Equal(1, results.Count(x => x));
        await using var check = Scope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.UserRoles.CountAsync(x => x.Role!.Name == Role.SuperAdmin));
    }

    [PostgresFact]
    public async Task Admin_cannot_touch_a_SuperAdmin_and_overrides_change_access()
    {
        var super = await CreateUserAsync("root@shop.nz", Role.SuperAdmin);
        var admin = await CreateUserAsync("admin@shop.nz", Role.Admin);
        var keeper = await CreateUserAsync("keeper2@shop.nz", Role.StoreKeeper);
        await using var scope = Scope(admin);
        var users = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        await Assert.ThrowsAsync<ForbiddenException>(() => users.ResetPasswordAsync(super, new ResetPasswordInput("another-long-password"), default));

        await users.SetPermissionsAsync(keeper, new UserPermissionsInput([
            new PermissionOverrideInput(PermissionKeys.CatalogManage, OverrideState.Deny),
            new PermissionOverrideInput(PermissionKeys.UsersView, OverrideState.Allow)]), default);
        var access = await scope.ServiceProvider.GetRequiredService<IAccessService>().GetAsync(keeper, default);
        Assert.False(access.Has(PermissionKeys.CatalogManage));
        Assert.True(access.Has(PermissionKeys.UsersView));
        var menus = await scope.ServiceProvider.GetRequiredService<IAuthService>().GetMenusAsync(keeper, default);
        Assert.DoesNotContain(menus, m => m.Key == "catalog");
        Assert.Contains(menus, m => m.Key == "admin");
    }

    [PostgresFact]
    public async Task Seeding_twice_changes_nothing_and_identity_rows_are_audited()
    {
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = (await db.Roles.CountAsync(), await db.Menus.CountAsync(), await db.RolePermissions.CountAsync());
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(default);
        Assert.Equal(before, (await db.Roles.CountAsync(), await db.Menus.CountAsync(), await db.RolePermissions.CountAsync()));
        Assert.All(await db.Roles.ToListAsync(), r => Assert.False(string.IsNullOrEmpty(r.CreatedBy)));
    }

    private sealed class TestActor : ICurrentActor { public string ActorId { get; set; } = "anonymous"; }

    private sealed class StubIssuer : IAccessTokenIssuer
    {
        public AccessToken Issue(AccessTokenRequest request) => new("test-access-token", DateTimeOffset.UtcNow.AddMinutes(10));
    }
}
