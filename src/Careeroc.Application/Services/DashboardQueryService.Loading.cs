using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

// Every database read the dashboard needs, in one place, with no projection logic.
public sealed partial class DashboardQueryService
{
    private sealed record DashboardData(
        List<DashboardInboxAccountRecord> VisibleInboxAccounts,
        List<JobApplication> Applications,
        List<DashboardReminderRecord> Reminders,
        HashSet<Guid> ApplicationIdsWithAnyLinkedEmail,
        HashSet<Guid> ApplicationIdsWithVisibleLinkedEmail,
        List<DashboardApplicationEmailSummaryRecord> ApplicationEmailSummaries,
        List<DashboardEmailRecord> VisibleEmails,
        DashboardInboxOverviewCounts InboxOverviewCounts,
        List<DashboardAnalysisJobRecord> AnalysisJobs);

    private async Task<DashboardData> LoadDashboardDataAsync(
        DashboardUserRecord user,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var visibleInboxAccounts = await _dbContext.InboxAccounts
            .AsNoTracking()
            .Where(account => account.UserId == user.Id)
            .Select(
                account => new DashboardInboxAccountRecord(
                    account.Id,
                    account.Provider,
                    account.EmailAddress,
                    account.OAuthMetadataJson,
                    account.LastSyncedAtUtc,
                    account.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        var visibleInboxAccountIds = visibleInboxAccounts
            .Select(account => account.Id)
            .ToHashSet();
        var allUserInboxAccountIds = await _dbContext.InboxAccounts
            .AsNoTracking()
            .Where(account => account.UserId == user.Id)
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);

        var applications = await _dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == user.Id)
            .OrderByDescending(application => application.LastActivityAtUtc)
            .ToListAsync(cancellationToken);

        var pendingActionCutoffUtc = nowUtc.Subtract(PendingActionOverdueWindow);
        var pendingActionMaxDueUtc = nowUtc.Add(PendingActionUpcomingWindow);
        var reminders = await _dbContext.Reminders
            .AsNoTracking()
            .Where(
                reminder =>
                    reminder.JobApplication.UserId == user.Id &&
                    (reminder.Status == ReminderStatus.Open ||
                     (reminder.Status == ReminderStatus.Snoozed && reminder.DueAtUtc <= nowUtc)) &&
                    reminder.DueAtUtc >= pendingActionCutoffUtc &&
                    reminder.DueAtUtc <= pendingActionMaxDueUtc &&
                    reminder.JobApplication.Status != ApplicationStatus.Rejected &&
                    reminder.JobApplication.Status != ApplicationStatus.Closed)
            .OrderBy(reminder => reminder.DueAtUtc)
            .Select(
                reminder => new DashboardReminderRecord(
                    reminder.Id,
                    reminder.JobApplicationId,
                    reminder.JobApplication.Company,
                    reminder.JobApplication.Role,
                    reminder.Type,
                    reminder.Title,
                    reminder.DueAtUtc,
                    reminder.Status))
            .ToListAsync(cancellationToken);

        var applicationIdsWithAnyLinkedEmail = allUserInboxAccountIds.Count == 0
            ? []
            : await _dbContext.InboxEmails
                .AsNoTracking()
                .Where(email => allUserInboxAccountIds.Contains(email.InboxAccountId) && email.JobApplicationId.HasValue)
                .Select(email => email.JobApplicationId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        var applicationIdsWithVisibleLinkedEmail = visibleInboxAccountIds.Count == 0
            ? []
            : await _dbContext.InboxEmails
                .AsNoTracking()
                .Where(email => visibleInboxAccountIds.Contains(email.InboxAccountId) && email.JobApplicationId.HasValue)
                .Select(email => email.JobApplicationId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        var applicationEmailSummaries = visibleInboxAccountIds.Count == 0
            ? []
            : await _dbContext.InboxEmails
                .AsNoTracking()
                .Where(email => visibleInboxAccountIds.Contains(email.InboxAccountId) && email.JobApplicationId.HasValue)
                .GroupBy(email => email.JobApplicationId!.Value)
                .Select(
                    group => new DashboardApplicationEmailSummaryRecord(
                        group.Key,
                        group.Min(email => email.ReceivedAtUtc),
                        group
                            .OrderByDescending(email => email.ReceivedAtUtc)
                            .Select(email => email.InboxAccount.Provider)
                            .FirstOrDefault(),
                        group
                            .OrderByDescending(email => email.ReceivedAtUtc)
                            .Select(email => email.InboxAccount.EmailAddress)
                            .FirstOrDefault(),
                        group
                            .OrderByDescending(email => email.ReceivedAtUtc)
                            .Select(email => email.ThreadId)
                            .FirstOrDefault(),
                        group
                            .OrderByDescending(email => email.ReceivedAtUtc)
                            .Select(email => email.ExternalMessageId)
                            .FirstOrDefault()))
                .ToListAsync(cancellationToken);
        var visibleEmails = visibleInboxAccountIds.Count == 0
            ? []
            : await _dbContext.InboxEmails
                .AsNoTracking()
                .Where(email => visibleInboxAccountIds.Contains(email.InboxAccountId) && email.JobApplicationId.HasValue)
                .OrderByDescending(email => email.ReceivedAtUtc)
                .Take(LinkedEmailFetchLimit)
                .Select(
                    email => new DashboardEmailRecord(
                        email.Id,
                        email.InboxAccountId,
                        email.JobApplicationId,
                        email.InboxAccount.Provider,
                        email.InboxAccount.EmailAddress,
                        email.ThreadId,
                        email.ExternalMessageId,
                        email.Subject,
                        email.Sender,
                        email.ReceivedAtUtc,
                        email.ProcessedAtUtc,
                        email.DueDateUtc,
                        email.Category,
                        email.ActionRequired,
                        email.RequiresManualReview,
                        email.ReviewReason,
                        email.ClassificationConfidence,
                        email.IsOutbound))
                .ToListAsync(cancellationToken);
        var inboxOverviewCounts = visibleInboxAccountIds.Count == 0
            ? new DashboardInboxOverviewCounts(0, 0, 0)
            : await _dbContext.InboxEmails
                .AsNoTracking()
                .Where(email => visibleInboxAccountIds.Contains(email.InboxAccountId))
                .GroupBy(_ => 1)
                .Select(
                    group => new DashboardInboxOverviewCounts(
                        group.Count(
                            email =>
                                !email.ProcessedAtUtc.HasValue ||
                                email.JobApplicationId.HasValue ||
                                email.Category.HasValue ||
                                email.ActionRequired ||
                                email.RequiresManualReview),
                        group.Count(
                            email =>
                                email.ProcessedAtUtc.HasValue &&
                                (email.JobApplicationId.HasValue ||
                                 email.Category.HasValue ||
                                 email.ActionRequired ||
                                 email.RequiresManualReview)),
                        group.Count(email => email.RequiresManualReview)))
                .FirstOrDefaultAsync(cancellationToken) ?? new DashboardInboxOverviewCounts(0, 0, 0);
        var analysisJobs = visibleInboxAccountIds.Count == 0
            ? []
            : await _dbContext.EmailProcessingJobs
                .AsNoTracking()
                .Where(
                    job =>
                        visibleInboxAccountIds.Contains(job.InboxEmail.InboxAccountId) &&
                        (!job.InboxEmail.ProcessedAtUtc.HasValue ||
                         job.InboxEmail.JobApplicationId.HasValue ||
                         job.InboxEmail.Category.HasValue ||
                         job.InboxEmail.ActionRequired ||
                         job.InboxEmail.RequiresManualReview))
                .Select(
                    job => new DashboardAnalysisJobRecord(
                        job.Status,
                        job.CompletedAtUtc,
                        job.UpdatedAtUtc,
                        job.LastError))
                .ToListAsync(cancellationToken);

        return new DashboardData(
            visibleInboxAccounts,
            applications,
            reminders,
            applicationIdsWithAnyLinkedEmail.ToHashSet(),
            applicationIdsWithVisibleLinkedEmail.ToHashSet(),
            applicationEmailSummaries,
            visibleEmails,
            inboxOverviewCounts,
            analysisJobs);
    }

    private async Task<List<DashboardEventRecord>> LoadRecentEventsAsync(
        IReadOnlySet<Guid> visibleApplicationIds,
        CancellationToken cancellationToken)
    {
        if (visibleApplicationIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.JobApplicationEvents
            .AsNoTracking()
            .Where(@event => visibleApplicationIds.Contains(@event.JobApplicationId))
            .OrderByDescending(@event => @event.EventTimeUtc)
            .Take(RecentActivityFetchLimit)
            .Select(
                @event => new DashboardEventRecord(
                    @event.Id,
                    @event.JobApplicationId,
                    @event.InboxEmailId,
                    @event.EventType,
                    @event.EventTimeUtc,
                    @event.JobApplication.Company,
                    @event.JobApplication.Role,
                    @event.InboxEmail == null
                        ? null
                        : @event.InboxEmail.ReceivedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
