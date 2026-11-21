// Creates the first SuperAdmin, once. Also seeds roles and menus.
// Usage: dotnet run --project tools/AdminBootstrap -- --email <address> [--name "Display name"]
// The password is read from the BOOTSTRAP_PASSWORD environment variable, or typed at a hidden prompt.
// Refuses to run when an active SuperAdmin already exists.
using Application.Common;
using Application.Identity;
using Domain.Identity;
using Infrastructure;
using Infrastructure.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

string? email = null, name = null;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--email") email = args[i + 1].Trim();
    if (args[i] == "--name") name = args[i + 1].Trim();
}
if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
{
    Console.Error.WriteLine("Usage: AdminBootstrap --email <address> [--name \"Display name\"]");
    return 2;
}

var config = new ConfigurationBuilder().AddUserSecrets(typeof(BootstrapActor).Assembly, optional: true).AddEnvironmentVariables().Build();
var connection = config["ConnectionStrings:DefaultConnection"];
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("Set ConnectionStrings:DefaultConnection with user-secrets or the ConnectionStrings__DefaultConnection environment variable.");
    return 2;
}

var password = Environment.GetEnvironmentVariable("BOOTSTRAP_PASSWORD");
if (string.IsNullOrEmpty(password))
{
    password = ReadHidden("Password for the new SuperAdmin: ");
    if (ReadHidden("Type it again: ") != password) { Console.Error.WriteLine("The passwords do not match."); return 2; }
}
if (password.Length < PasswordRules.MinLength) { Console.Error.WriteLine($"The password must be at least {PasswordRules.MinLength} characters."); return 2; }

var services = new ServiceCollection();
services.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddSingleton(TimeProvider.System);
services.AddSingleton<ICurrentActor, BootstrapActor>();
services.AddScoped<AuditSaveChangesInterceptor>();
services.AddDbContext<AppDbContext>((sp, o) => o.UseNpgsql(connection).AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
services.AddIdentityInfrastructure(config.GetSection("Auth").Get<SessionSettings>() ?? new SessionSettings());
services.AddSingleton<IAccessTokenIssuer, NoTokens>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

try
{
    if ((await db.Database.GetPendingMigrationsAsync()).Any())
    {
        Console.Error.WriteLine("The database has pending migrations. Apply them first (dotnet ef database update).");
        return 1;
    }
    await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(default);

    await using var transaction = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(7340001)");
    var superRole = await db.Roles.SingleAsync(x => x.NormalizedName == "SUPERADMIN");
    var hasSuperAdmin = await db.UserRoles.AnyAsync(r => r.RoleId == superRole.Id && db.Users.Any(u => u.Id == r.UserId && u.IsActive));
    if (hasSuperAdmin)
    {
        Console.Error.WriteLine("An active SuperAdmin already exists. Use the admin app to add more users.");
        return 1;
    }
    var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    var user = new User { Email = email, DisplayName = string.IsNullOrWhiteSpace(name) ? email : name };
    var result = await users.CreateAsync(user, password);
    if (!result.Succeeded)
    {
        foreach (var error in result.Errors) Console.Error.WriteLine(error.Description);
        return 1;
    }
    db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = superRole.Id });
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    Console.WriteLine($"Created SuperAdmin {user.Email}. Sign in to the admin app with this address.");
    return 0;
}
catch (Exception ex)
{
    // Type only: provider messages can contain connection details.
    Console.Error.WriteLine($"Bootstrap failed ({ex.GetType().Name}). Nothing was created.");
    return 1;
}

static string ReadHidden(string prompt)
{
    Console.Write(prompt);
    var chars = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); continue; }
        if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
    }
    Console.WriteLine();
    return new string(chars.ToArray());
}

internal sealed class BootstrapActor : ICurrentActor { public string ActorId => "system:bootstrap"; }

internal sealed class NoTokens : IAccessTokenIssuer
{
    public AccessToken Issue(AccessTokenRequest request) => throw new NotSupportedException();
}
