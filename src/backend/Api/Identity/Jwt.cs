using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Identity;

/// <summary>Bound from the "Jwt" section. SigningKey comes from user-secrets or environment variables only.</summary>
public sealed class JwtSettings
{
    public string Issuer { get; set; } = "spareparts-api";
    public string Audience { get; set; } = "spareparts-admin";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 10;

    public const int MinKeyBytes = 32;

    public static bool IsValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && Encoding.UTF8.GetByteCount(key) >= MinKeyBytes;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey)) { KeyId = "spareparts-1" };

    /// <summary>Development and tests may run without a configured key; sessions then end when the API restarts.</summary>
    public static string EphemeralKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

public sealed class AccessTokenIssuer(JwtSettings settings, TimeProvider clock) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler handler = new();

    public AccessToken Issue(AccessTokenRequest request)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(AuthClaims.Subject, request.UserId.ToString()),
                new Claim(AuthClaims.Session, request.FamilyId.ToString()),
                new Claim(AuthClaims.Stamp, request.StampHash),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(settings.Key(), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expires);
    }
}

public static class JwtSetup
{
    public static void Configure(JwtBearerOptions options, JwtSettings settings)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = settings.Key(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AuthClaims.Subject,
        };
        options.Events = new JwtBearerEvents
        {
            // A token stops working within the access cache window after deactivation,
            // deletion, password change or sign-out everywhere (the security stamp changes).
            OnTokenValidated = async context =>
            {
                var access = context.HttpContext.RequestServices.GetRequiredService<IAccessService>();
                var userId = context.Principal?.UserId();
                var stamp = context.Principal?.FindFirstValue(AuthClaims.Stamp);
                if (userId is null || stamp is null) { context.Fail("Invalid token."); return; }
                var snapshot = await access.GetAsync(userId.Value, context.HttpContext.RequestAborted);
                if (!snapshot.IsActive || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(snapshot.StampHash), Encoding.UTF8.GetBytes(stamp)))
                    context.Fail("Session ended.");
            },
        };
    }
}
