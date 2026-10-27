using Careeroc.Application.Services;
using Careeroc.Application.Abstractions;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Api.Background;

public sealed class InboxSyncWorker : BackgroundService
{
    private const int DefaultIntervalSeconds = 900;
    private const int DevelopmentMinimumIntervalSeconds = 30;
    private const int ProductionMinimumIntervalSeconds = 900;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IClock _clock;
    private readonly ILogger<InboxSyncWorker> _logger;

    public InboxSyncWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IClock clock,
        ILogger<InboxSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue<bool>("SyncWorker:Enabled"))
        {
            return;
        }

        var intervalSeconds = ResolveIntervalSeconds();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        if (ShouldRunOnStartup())
        {
            await RunOnceAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    private int ResolveIntervalSeconds()
    {
        var configuredInterval = _configuration.GetValue("SyncWorker:IntervalSeconds", DefaultIntervalSeconds);
        var minimumInterval = _environment.IsDevelopment()
            ? DevelopmentMinimumIntervalSeconds
            : ProductionMinimumIntervalSeconds;

        return Math.Max(minimumInterval, configuredInterval);
    }

    private bool ShouldRunOnStartup()
    {
        return _configuration.GetValue("SyncWorker:RunOnStartup", _environment.IsDevelopment());
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // A null user syncs every connected inbox; a self-hosted install has no idle tenants to skip.
            var userResults = new[] { await ProcessUserAsync(null, cancellationToken) };

            _logger.LogInformation(
                "Automatic inbox sync finished. Created {EmailsCreated} emails, enqueued {JobsEnqueued} jobs, queued {StaleEmailsQueued} stale analysis jobs, processed {JobsProcessed} jobs, refreshed {RemindersRefreshed} reminder due dates, and updated {EmailsUpdated} source emails.",
                userResults.Sum(result => result.EmailsCreated),
                userResults.Sum(result => result.JobsEnqueued),
                userResults.Sum(result => result.StaleEmailsQueued),
                userResults.Sum(result => result.JobsProcessed),
                userResults.Sum(result => result.RemindersRefreshed),
                userResults.Sum(result => result.EmailsUpdated));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Automatic inbox sync failed.");
        }
    }

    private async Task<WorkerUserSyncResult> ProcessUserAsync(
        string? userEmail,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var inboxSyncCoordinator = scope.ServiceProvider.GetRequiredService<InboxSyncCoordinator>();

        var startedAtUtc = _clock.UtcNow;
        var result = await inboxSyncCoordinator.RunForUserAsync(userEmail, cancellationToken);

        _logger.LogInformation(
            "Automatic inbox sync finished for user {UserEmail} in {ElapsedMs}ms.",
            userEmail ?? "(all users)",
            (_clock.UtcNow - startedAtUtc).TotalMilliseconds);

        return new WorkerUserSyncResult(
            result.EmailsCreated,
            result.JobsEnqueued,
            result.StaleEmailsQueued,
            result.JobsProcessed,
            result.RemindersRefreshed,
            result.EmailsUpdated);
    }

    private sealed record WorkerUserSyncResult(
        int EmailsCreated,
        int JobsEnqueued,
        int StaleEmailsQueued,
        int JobsProcessed,
        int RemindersRefreshed,
        int EmailsUpdated);
}
