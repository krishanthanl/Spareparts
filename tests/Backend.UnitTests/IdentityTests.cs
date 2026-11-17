using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Identity;
using Application.Identity;
using Domain.Identity;
using Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using P = Application.Identity.PermissionKeys;

namespace Backend.UnitTests;

public sealed class PermissionRuleTests
{
    [Fact]
    public void Deny_beats_role_permission_and_allow_adds_one()
    {
        var result = AccessService.Evaluate(false, [P.CatalogManage, P.DashboardView],
            [(P.CatalogManage, PermissionEffect.Deny), (P.UsersView, PermissionEffect.Allow)]);
        Assert.DoesNotContain(P.CatalogManage, result);
        Assert.Contains(P.UsersView, result);
        Assert.Contains(P.DashboardView, result);
    }

    [Fact]
    public void Deny_beats_allow_for_the_same_key_and_missing_override_inherits()
    {
        Assert.Empty(AccessService.Evaluate(false, [], [(P.UsersView, PermissionEffect.Allow), (P.UsersView, PermissionEffect.Deny)]));
        Assert.Equal([P.DashboardView], AccessService.Evaluate(false, [P.DashboardView], []));
    }

    [Fact]
    public void SuperAdmin_has_everything_and_cannot_be_denied()
    {
        var result = AccessService.Evaluate(true, [], [(P.UsersManage, PermissionEffect.Deny)]);
        Assert.Equal(P.All.Count, result.Count);
    }

    [Fact]
    public void Unknown_keys_from_the_database_are_ignored()
    {
        var result = AccessService.Evaluate(false, ["old.key"], [("other.key", PermissionEffect.Allow)]);
        Assert.Empty(result);
    }

    [Fact]
    public void Inactive_snapshot_has_no_permissions()
    {
        var snapshot = new AccessSnapshot(Guid.NewGuid(), false, "", true, new HashSet<string> { P.UsersView });
        Assert.False(snapshot.Has(P.UsersView));
    }

    [Fact]
    public void Default_roles_only_use_known_keys()
    {
        foreach (var role in new[] { Role.Admin, Role.StoreKeeper, Role.Cashier })
            Assert.All(IdentitySeeder.DefaultPermissions(role), key => Assert.True(P.IsKnown(key), key));
        Assert.DoesNotContain(P.RefundsApprove, IdentitySeeder.DefaultPermissions(Role.Cashier));
    }
}

public sealed class IdentityValidationTests
{
    [Theory]
    [InlineData("short", false)]
    [InlineData("eleven-char", false)]
    [InlineData("twelve-chars", true)]
    public void New_passwords_need_twelve_characters(string password, bool valid) =>
        Assert.Equal(valid, new ResetPasswordInputValidator().Validate(new ResetPasswordInput(password)).IsValid);

    [Fact]
    public void Temporary_password_cannot_be_the_email()
    {
        var result = new UserCreateInputValidator().Validate(new UserCreateInput("staff@shop.co.nz", "Staff", "STAFF@shop.co.nz", null));
        Assert.Contains(result.Errors, e => e.PropertyName == "TemporaryPassword");
    }

    [Fact]
    public void Role_permissions_must_be_known_and_distinct()
    {
        Assert.False(new RoleInputValidator().Validate(new RoleInput("Clerk", null, ["made.up"], null)).IsValid);
        Assert.False(new RoleInputValidator().Validate(new RoleInput("Clerk", null, [P.UsersView, P.UsersView], null)).IsValid);
        Assert.True(new RoleInputValidator().Validate(new RoleInput("Clerk", null, [P.UsersView], null)).IsValid);
    }

    [Fact]
    public void Override_keys_must_be_known()
    {
        var input = new UserPermissionsInput([new PermissionOverrideInput("made.up", OverrideState.Allow)]);
        Assert.False(new UserPermissionsInputValidator().Validate(input).IsValid);
    }

    [Fact]
    public void User_list_sort_is_whitelisted()
    {
        Assert.False(new UserListRequestValidator().Validate(new UserListRequest(Sort: "PasswordHash")).IsValid);
        Assert.True(new UserListRequestValidator().Validate(new UserListRequest(Sort: "-email")).IsValid);
    }
}

public sealed class AccessTokenTests
{
    [Fact]
    public async Task Valid_token_with_permission_reaches_the_endpoint()
    {
        await using var factory = new AuthFactory();
        var response = await factory.ClientWith(factory.TokenFor(P.CatalogManage)).GetAsync("/api/items?sort=bad");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // past authorization, stopped by validation
    }

    [Fact]
    public async Task Missing_token_is_401_and_missing_permission_is_403()
    {
        await using var factory = new AuthFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.ClientWith(factory.TokenFor(P.CatalogManage)).GetAsync("/api/users")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/users")]
    [InlineData("POST", "/api/users")]
    [InlineData("GET", "/api/roles")]
    [InlineData("PUT", "/api/users/6c8f8f0e-4a3b-4c55-9d0f-0e1a2b3c4d5e/permissions")]
    [InlineData("GET", "/api/categories")]
    public async Task Apis_behind_hidden_menus_are_forbidden_when_called_directly(string method, string url)
    {
        await using var factory = new AuthFactory();
        var client = factory.ClientWith(factory.TokenFor(P.DashboardView));
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "GET" ? null : JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewing_deleted_records_needs_its_own_permission()
    {
        await using var factory = new AuthFactory();
        var client = factory.ClientWith(factory.TokenFor(P.CatalogManage));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/items?includeDeleted=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/categories?includeDeleted=true")).StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        await using var factory = new AuthFactory();
        var user = factory.Access.Add([P.CatalogManage]);
        var token = factory.TokenFor(user, new FixedClock(DateTimeOffset.UtcNow.AddHours(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientWith(token).GetAsync("/api/items")).StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_another_key_or_for_another_audience_is_rejected()
    {
        await using var factory = new AuthFactory();
        var user = factory.Access.Add([P.CatalogManage]);
        var otherKey = new JwtSettings { SigningKey = JwtSettings.EphemeralKey(), Issuer = factory.Jwt.Issuer, Audience = factory.Jwt.Audience };
        var otherAudience = new JwtSettings { SigningKey = factory.Jwt.SigningKey, Issuer = factory.Jwt.Issuer, Audience = "someone-else" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientWith(factory.TokenFor(user, jwt: otherKey)).GetAsync("/api/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientWith(factory.TokenFor(user, jwt: otherAudience)).GetAsync("/api/items")).StatusCode);
    }

    [Fact]
    public async Task Token_stops_working_when_user_is_deactivated_or_stamp_changes()
    {
        await using var factory = new AuthFactory();
        var user = factory.Access.Add([P.CatalogManage]);
        var client = factory.ClientWith(factory.TokenFor(user));
        factory.Access.Set(user with { StampHash = "CHANGED000000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/items")).StatusCode);
        factory.Access.Set(user with { IsActive = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/items")).StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_passes_every_policy()
    {
        await using var factory = new AuthFactory();
        var user = factory.Access.Add([], superAdmin: true);
        var response = await factory.ClientWith(factory.TokenFor(user)).GetAsync("/api/users?sort=bad");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Production_refuses_to_start_without_a_signing_key()
    {
        using var factory = new AuthFactory("Production", new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = "",
            ["ConnectionStrings:DefaultConnection"] = "Host=db.example;Database=x;Username=u;SSL Mode=VerifyFull",
            ["AllowedHosts"] = "*",
        });
        var error = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("Jwt:SigningKey", error.Message);
    }
}

public sealed class AuthEndpointTests
{
    private static HttpRequestMessage Post(string url, string? origin = "http://localhost:5173", bool csrfHeader = true, string? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (csrfHeader) request.Headers.Add(RequireSameOriginAttribute.Header, "1");
        if (cookie is not null) request.Headers.Add("Cookie", $"{RefreshCookie.Name}={cookie}");
        return request;
    }

    [Theory]
    [InlineData("http://localhost:5173", false)]
    [InlineData("https://evil.example", true)]
    [InlineData(null, true)]
    public async Task Refresh_and_logout_need_the_admin_origin_and_csrf_header(string? origin, bool csrfHeader)
    {
        await using var factory = new AuthFactory { Auth = new FakeAuthService() };
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Post("/api/auth/refresh", origin, csrfHeader, "abc"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Post("/api/auth/logout", origin, csrfHeader, "abc"))).StatusCode);
    }

    [Fact]
    public async Task Refresh_reads_the_cookie_and_sets_a_strict_http_only_cookie()
    {
        var auth = new FakeAuthService();
        await using var factory = new AuthFactory { Auth = auth };
        var response = await factory.CreateClient().SendAsync(Post("/api/auth/refresh", cookie: "old-token"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("old-token", auth.LastRefreshToken);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("sp_refresh=refresh-token-value", cookie);
        foreach (var part in new[] { "httponly", "secure", "samesite=strict", "path=/api/auth" })
            Assert.Contains(part, cookie.ToLowerInvariant());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("refreshToken", out _)); // the refresh token never appears in the body
    }

    [Fact]
    public async Task Failed_login_is_a_generic_401()
    {
        await using var factory = new AuthFactory { Auth = new FakeAuthService { FailLogin = true } };
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "x@shop.nz", password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Email or password is incorrect.", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_is_rate_limited_per_ip()
    {
        var auth = new FakeAuthService { FailLogin = true };
        await using var factory = new AuthFactory { Auth = auth };
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = $"u{i}@shop.nz", password = "wrong-password" })).StatusCode);
        Assert.Equal(10, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Equal(10, auth.LoginCalls);
    }

    [Fact]
    public async Task Empty_login_is_a_validation_error_without_calling_the_service()
    {
        var auth = new FakeAuthService();
        await using var factory = new AuthFactory { Auth = auth };
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, auth.LoginCalls);
    }

    [Fact]
    public async Task Me_requires_sign_in()
    {
        await using var factory = new AuthFactory { Auth = new FakeAuthService() };
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.ClientWith(factory.TokenFor()).GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public void Identity_password_and_lockout_options_match_the_policy()
    {
        var options = new Microsoft.AspNetCore.Identity.IdentityOptions();
        Infrastructure.DependencyInjection.ConfigureIdentity(options, new SessionSettings());
        Assert.Equal(12, options.Password.RequiredLength);
        Assert.False(options.Password.RequireNonAlphanumeric);
        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
    }
}
