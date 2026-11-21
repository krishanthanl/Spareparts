using Infrastructure.Identity;

namespace Api.Identity;

/// <summary>
/// Keeps roles and menus in step with code at startup, then purges old refresh tokens.
/// Runs in the background and never stops the API from starting if the database is unavailable.
/// </summary>
public sealed class IdentitySeedService(IServiceScopeFactory scopes, ILogger<IdentitySeedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
            await seeder.SeedAsync(stoppingToken);
            var purged = await seeder.PurgeExpiredTokensAsync(stoppingToken);
            logger.LogInformation("Roles and menus are up to date; {Purged} old refresh tokens removed.", purged);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Type only: provider messages can contain connection details.
            logger.LogWarning("Role and menu seeding skipped ({ExceptionType}). It runs again on next start.", ex.GetType().Name);
        }
    }
}
