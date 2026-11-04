using Careeroc.Application.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Careeroc.Application.Services;

public sealed class InboxSyncCoordinator
{
    private readonly EmailSyncService _emailSyncService;
    private readonly EmailAnalysisMaintenanceService _emailAnalysisMaintenanceService;
    private readonly EmailIntelligenceService _emailIntelligenceService;
    private readonly ReminderEvidenceRefreshService _reminderEvidenceRefreshService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InboxSyncCoordinator> _logger;

    public InboxSyncCoordinator(
        EmailSyncService emailSyncService,
        EmailAnalysisMaintenanceService emailAnalysisMaintenanceService,
        EmailIntelligenceService emailIntelligenceService,
        ReminderEvidenceRefreshService reminderEvidenceRefreshService,
        IConfiguration configuration,
        ILogger<InboxSyncCoordinator> logger)
    {
        _emailSyncService = emailSyncService;
        _emailAnalysisMaintenanceService = emailAnalysisMaintenanceService;
        _emailIntelligenceService = emailIntelligenceService;
        _reminderEvidenceRefreshService = reminderEvidenceRefreshService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<InboxSyncCoordinatorResult> RunForUserAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var sync = await _emailSyncService.RunOnceAsync(userEmail, cancellationToken);
        var staleEmailsQueued = await _emailAnalysisMaintenanceService.QueueStaleEmailsAsync(
            userEmail,
            cancellationToken: cancellationToken);
        var processing = await _emailIntelligenceService.ProcessPendingUntilIdleAsync(
            _configuration.GetValue("EmailProcessing:WorkerBatchSize", 25),
            _configuration.GetValue("EmailProcessing:WorkerMaxBatches", 40),
            userEmail,
            cancellationToken);
        var reminderRefresh = await _reminderEvidenceRefreshService.RefreshPendingRemindersAsync(
            userEmail,
            cancellationToken);

        _logger.LogInformation(
            "Inbox sync coordinator finished for user {UserEmail}. Created {EmailsCreated} emails, enqueued {JobsEnqueued} jobs, queued {StaleEmailsQueued} stale analysis jobs, processed {JobsProcessed} jobs, refreshed {RemindersRefreshed} reminders, updated {EmailsUpdated} source emails.",
            userEmail ?? "(all users)",
            sync.EmailsCreated,
            sync.JobsEnqueued,
            staleEmailsQueued,
            processing.JobsProcessed,
            reminderRefresh.RemindersRefreshed,
            reminderRefresh.EmailsUpdated);

        return new InboxSyncCoordinatorResult(
            sync.EmailsCreated,
            sync.JobsEnqueued,
            staleEmailsQueued,
            processing.JobsProcessed,
            reminderRefresh.RemindersRefreshed,
            reminderRefresh.EmailsUpdated);
    }
}
