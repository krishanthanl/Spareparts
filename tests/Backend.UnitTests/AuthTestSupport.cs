using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Api.Identity;
using Application.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.UnitTests;

/// <summary>
/// Runs the real API with real JWT validation and permission policies. Only the database-backed
/// access lookup (and optionally the auth service) is replaced, because no database is available here.
/// </summary>
public class AuthFactory(string environment = "Testing", IDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public FakeAccess Access { get; } = new();
    public FakeAuthService? Auth { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Timeout=1;Pooling=false",
                ["Cors:Origins:0"] = "http://localhost:5173",
                ["Storage:LocalRoot"] = Path.Combine(Path.GetTempPath(), "sp-tests-media"),
                ["Jwt:SigningKey"] = "unit-test-signing-key-that-is-long-enough-0123456789",
            };
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>()) values[key] = value;
            config.AddInMemoryCollection(values);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccessService>();
            services.AddSingleton<IAccessService>(Access);
            if (Auth is not null)
            {
                services.RemoveAll<IAuthService>();
                services.AddSingleton<IAuthService>(Auth);
            }
        });
    }

    public JwtSettings Jwt => Services.GetRequiredService<JwtSettings>();

    /// <summary>A signed-in user holding exactly these permissions.</summary>
    public string TokenFor(params string[] permissions) => TokenFor(Access.Add(permissions));

    public string TokenFor(AccessSnapshot user, TimeProvider? clock = null, JwtSettings? jwt = null) =>
        new AccessTokenIssuer(jwt ?? Jwt, clock ?? TimeProvider.System).Issue(new AccessTokenRequest(user.UserId, Guid.NewGuid(), user.StampHash)).Token;

    public HttpClient ClientWith(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>The old catalogue tests' factory: signed in with one permission, or none.</summary>
public sealed class SignedInFactory(string? permission) : AuthFactory
{
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", permission is null ? TokenFor() : TokenFor(permission));
    }
}

public sealed class FakeAccess : IAccessService
{
    private readonly ConcurrentDictionary<Guid, AccessSnapshot> users = new();

    public AccessSnapshot Add(string[] permissions, bool active = true, bool superAdmin = false)
    {
        var snapshot = new AccessSnapshot(Guid.NewGuid(), active, "STAMP0000000000A", superAdmin, permissions.ToHashSet());
        users[snapshot.UserId] = snapshot;
        return snapshot;
    }

    public void Set(AccessSnapshot snapshot) => users[snapshot.UserId] = snapshot;

    public Task<AccessSnapshot> GetAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(users.TryGetValue(userId, out var s) ? s : new AccessSnapshot(userId, false, "", false, new HashSet<string>()));

    public void Invalidate(Guid userId) { }
    public void InvalidateAll() { }
}

public sealed class FakeAuthService : IAuthService
{
    public int LoginCalls;
    public string? LastRefreshToken;
    public bool FailLogin { get; init; }

    private static AuthSession Session() => new(
        new AuthResponse("access", DateTimeOffset.UtcNow.AddMinutes(10), new SessionUser(Guid.NewGuid(), "a@shop.nz", "A", false, [], [])),
        "refresh-token-value", DateTimeOffset.UtcNow.AddDays(14));

    public Task<AuthSession> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct)
    {
        Interlocked.Increment(ref LoginCalls);
        if (FailLogin) throw new Application.Common.AuthenticationFailedException();
        return Task.FromResult(Session());
    }

    public Task<AuthSession> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken ct)
    {
        LastRefreshToken = refreshToken;
        return Task.FromResult(Session());
    }

    public Task LogoutAsync(string? refreshToken, CancellationToken ct) => Task.CompletedTask;
    public Task LogoutAllAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
    public Task<AuthSession> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, ClientInfo client, CancellationToken ct) => Task.FromResult(Session());
    public Task<SessionUser> GetSessionUserAsync(Guid userId, CancellationToken ct) => Task.FromResult(Session().Response.User);
    public Task<IReadOnlyList<MenuNode>> GetMenusAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<MenuNode>>([]);
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
