using Infrastructure.Sales;

namespace Api;

public sealed class CheckoutExpiryWorker(IServiceScopeFactory scopes, ILogger<CheckoutExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CheckoutOrders>().ExpireAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(error, "Checkout reservation expiry failed. The worker will retry.");
            }
        }
    }
}
