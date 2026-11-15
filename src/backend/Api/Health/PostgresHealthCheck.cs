using Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Health;

public sealed class PostgresHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { return await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database unavailable."); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return HealthCheckResult.Unhealthy("Database unavailable."); }
    }
}
