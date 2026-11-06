using Careeroc.Application.Services;

namespace Careeroc.Api.Background;

// Runs the syncs that SyncNowQueue holds, a few at a time, and stops with the host instead of
// leaving detached tasks behind. It runs even when the periodic InboxSyncWorker is off.
public sealed class SyncNowWorker(
    SyncNowQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<SyncNowWorker> logger) : BackgroundService
{
    private const int MaxConcurrentSyncs = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Parallel.ForEachAsync(
                queue.ReadAllAsync(stoppingToken),
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentSyncs, CancellationToken = stoppingToken },
                async (userEmail, cancellationToken) =>
                {
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var coordinator = scope.ServiceProvider.GetRequiredService<InboxSyncCoordinator>();
                        await coordinator.RunForUserAsync(userEmail, cancellationToken);
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogWarning(exception, "A user-triggered inbox sync failed.");
                    }
                });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
