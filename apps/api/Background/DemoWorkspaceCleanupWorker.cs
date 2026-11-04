using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Microsoft.Extensions.Options;

namespace Careeroc.Api.Background;

// Deletes demo workspaces after their lifetime so the shared demo database stays small.
public sealed class DemoWorkspaceCleanupWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DemoOptions> _options;
    private readonly ILogger<DemoWorkspaceCleanupWorker> _logger;

    public DemoWorkspaceCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<DemoOptions> options,
        ILogger<DemoWorkspaceCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var deleted = await scope.ServiceProvider
                    .GetRequiredService<DemoWorkspaceService>()
                    .DeleteExpiredWorkspacesAsync(stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation("Deleted {Count} expired demo workspaces.", deleted);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Demo workspace cleanup failed; retrying on the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
