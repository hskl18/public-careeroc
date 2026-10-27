using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class DashboardInboxCheckService
{
    private const int ProcessingBatchSize = 25;
    private const int MaxProcessingBatches = 8;
    private const int MaxStaleEmailsQueued = 100;

    private readonly ICareerocDbContext _dbContext;
    private readonly EmailSyncService _emailSyncService;
    private readonly EmailAnalysisMaintenanceService _emailAnalysisMaintenanceService;
    private readonly EmailIntelligenceService _emailIntelligenceService;
    private readonly ReminderEvidenceRefreshService _reminderEvidenceRefreshService;
    private readonly DashboardQueryService _dashboardQueryService;
    private readonly IClock _clock;

    public DashboardInboxCheckService(
        ICareerocDbContext dbContext,
        EmailSyncService emailSyncService,
        EmailAnalysisMaintenanceService emailAnalysisMaintenanceService,
        EmailIntelligenceService emailIntelligenceService,
        ReminderEvidenceRefreshService reminderEvidenceRefreshService,
        DashboardQueryService dashboardQueryService,
        IClock clock)
    {
        _dbContext = dbContext;
        _emailSyncService = emailSyncService;
        _emailAnalysisMaintenanceService = emailAnalysisMaintenanceService;
        _emailIntelligenceService = emailIntelligenceService;
        _reminderEvidenceRefreshService = reminderEvidenceRefreshService;
        _dashboardQueryService = dashboardQueryService;
        _clock = clock;
    }

    public async Task<DashboardInboxCheckResult> CheckAsync(
        string? userEmail,
        TimeSpan cacheWindow,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var syncableAccounts = await GetSyncableGmailAccountsAsync(resolvedUserEmail, cancellationToken);
        var cacheCutoffUtc = _clock.UtcNow.Subtract(cacheWindow);
        var shouldFetchInbox = syncableAccounts.Any(
            account =>
                !account.LastSyncedAtUtc.HasValue ||
                account.LastSyncedAtUtc.Value <= cacheCutoffUtc);
        var sync = shouldFetchInbox
            ? await _emailSyncService.RunOnceAsync(resolvedUserEmail, cacheWindow, cancellationToken)
            : new EmailSyncResult(0, 0);

        var staleEmailsQueued = await _emailAnalysisMaintenanceService.QueueStaleEmailsAsync(
            resolvedUserEmail,
            MaxStaleEmailsQueued,
            cancellationToken);
        var processing = await _emailIntelligenceService.ProcessPendingUntilIdleAsync(
            ProcessingBatchSize,
            MaxProcessingBatches,
            resolvedUserEmail,
            cancellationToken);
        await _reminderEvidenceRefreshService.RefreshPendingRemindersAsync(
            resolvedUserEmail,
            cancellationToken);
        var dashboard = await _dashboardQueryService.GetDashboardAsync(resolvedUserEmail, cancellationToken);

        return new DashboardInboxCheckResult(
            CacheHit: !shouldFetchInbox,
            Sync: sync,
            StaleEmailsQueued: staleEmailsQueued,
            Processing: processing,
            Dashboard: dashboard);
    }

    private async Task<IReadOnlyList<InboxAccount>> GetSyncableGmailAccountsAsync(
        string? resolvedUserEmail,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.InboxAccounts
            .AsNoTracking()
            .Where(account => account.Provider == ApplicationDefaults.GmailInboxProvider);

        if (!string.IsNullOrWhiteSpace(resolvedUserEmail))
        {
            query = query.Where(account => account.User.Email == resolvedUserEmail);
        }

        var accounts = await query.ToListAsync(cancellationToken);

        return accounts
            .Where(EmailSyncService.CanPullFromInbox)
            .ToList();
    }

    private static string? ResolveUserEmail(string? userEmail)
    {
        return ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
    }
}
