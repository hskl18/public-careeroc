using System.Globalization;
using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Applications;
using Careeroc.Contracts.Dashboard;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class DashboardQueryService
{
    private const int RecentActivityLimit = 50;
    private const int RecentActivityFetchLimit = 250;
    private const int LinkedEmailFetchLimit = 2000;
    private static readonly TimeSpan PendingActionOverdueWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan PendingActionUpcomingWindow = TimeSpan.FromDays(14);

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public DashboardQueryService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public Task<DashboardResponse> GetDashboardAsync(
        string? userEmail,
        CancellationToken cancellationToken)
    {
        return GetDashboardAsync(userEmail, null, cancellationToken);
    }

    public async Task<DashboardResponse> GetDashboardAsync(
        string? userEmail = null,
        DashboardQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = CareerocTelemetry.ActivitySource.StartActivity("dashboard.query");
        activity?.SetTag("user.email", userEmail);
        activity?.SetTag("dashboard.view", options?.View);

        options ??= DashboardQueryOptions.Empty;
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.Email == resolvedUserEmail)
            .Select(candidate => new DashboardUserRecord(candidate.Id, candidate.Email))
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return new DashboardResponse(
                new DashboardMetricsDto(0, 0, 0, 0, 0, 0, 0, 0, 0),
                new InboxOverviewDto(
                    resolvedUserEmail,
                    0,
                    0,
                    null,
                    ScannedCount: 0,
                    TotalEstimate: 0,
                    RecognizedApplications: 0,
                    BackfillStatus: "idle"),
                [],
                [],
                []);
        }

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

        var nowUtc = _clock.UtcNow;
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
        var applicationReceivedTimes = applicationEmailSummaries
            .ToDictionary(
                summary => summary.JobApplicationId,
                summary => summary.FirstReceivedAtUtc);
        var applicationGmailUrls = applicationEmailSummaries
            .Where(summary => !string.IsNullOrWhiteSpace(summary.GmailUrl))
            .ToDictionary(
                summary => summary.JobApplicationId,
                summary => summary.GmailUrl);
        var applicationActionGmailUrls = visibleEmails
            .GroupBy(email => email.JobApplicationId)
            .Where(group => group.Key.HasValue)
            .ToDictionary(
                group => group.Key!.Value,
                group => group.ToList());
        var applicationLastOutboundReplyAtUtc = visibleEmails
            .Where(email => email.JobApplicationId.HasValue && email.IsOutbound)
            .GroupBy(email => email.JobApplicationId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (DateTimeOffset?)group.Max(email => email.ReceivedAtUtc));
        var applicationEmailsById = visibleEmails
            .Where(email => email.JobApplicationId.HasValue)
            .GroupBy(email => email.JobApplicationId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var applicationReviewStates = applicationEmailsById
            .ToDictionary(
                group => group.Key,
                group => BuildApplicationReviewState(group.Value));
        foreach (var application in applications)
        {
            if (TryBuildIdentityReviewState(application, out var identityReviewState))
            {
                applicationReviewStates[application.Id] =
                    MergeApplicationReviewState(
                        applicationReviewStates.GetValueOrDefault(application.Id),
                        identityReviewState);
            }
        }
        var applicationIdsWithAnyLinkedEmailSet = applicationIdsWithAnyLinkedEmail.ToHashSet();
        var applicationIdsWithVisibleLinkedEmailSet = applicationIdsWithVisibleLinkedEmail.ToHashSet();
        var visibleApplicationIds = applications
            .Where(
                application =>
                    !applicationIdsWithAnyLinkedEmailSet.Contains(application.Id) ||
                    applicationIdsWithVisibleLinkedEmailSet.Contains(application.Id))
            .Select(application => application.Id)
            .ToHashSet();

        var events = visibleApplicationIds.Count == 0
            ? []
            : await _dbContext.JobApplicationEvents
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

        applications = applications
            .Where(application => visibleApplicationIds.Contains(application.Id))
            .ToList();

        var visibleActionReminders = reminders
            .Where(
                reminder =>
                    visibleApplicationIds.Contains(reminder.JobApplicationId) &&
                    !IsLowSignalFallbackReminder(reminder))
            .ToList();
        var visibleActionReminderRecords = visibleActionReminders
            .Select(
                reminder =>
                {
                    applicationActionGmailUrls.TryGetValue(reminder.JobApplicationId, out var reminderEmails);
                    var evidenceEmail = ResolveReminderEvidence(reminder, reminderEmails ?? []);
                    return new DashboardPendingActionRecord(reminder, evidenceEmail);
                })
            .Where(record => record.EvidenceEmail?.RequiresManualReview != true)
            .ToList();
        var applicationIdsWithOpenReminder = visibleActionReminderRecords
            .Select(record => record.Reminder.JobApplicationId)
            .ToHashSet();
        var nextReminderByApplicationId = visibleActionReminderRecords
            .GroupBy(record => record.Reminder.JobApplicationId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(record => record.Reminder.DueAtUtc)
                    .First());
        var pendingActionRecords = visibleActionReminderRecords
            .Take(6)
            .ToList();

        var visibleEvents = events;
        var emailRecordsById = visibleEmails.ToDictionary(email => email.Id);
        var latestEvidenceByApplication = visibleEvents
            .Where(@event => IsDecisionEvidenceEvent(@event.EventType) && @event.InboxEmailId.HasValue)
            .Select(
                @event =>
                {
                    emailRecordsById.TryGetValue(@event.InboxEmailId!.Value, out var email);
                    return email is null
                        ? null
                        : new
                        {
                            @event.JobApplicationId,
                            Evidence = BuildLatestEvidence(@event, email)
                        };
                })
            .Where(record => record is not null)
            .GroupBy(record => record!.JobApplicationId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(record => record!.Evidence.OccurredAtUtc)
                    .First()!
                    .Evidence);
        events = visibleEvents
            .Take(RecentActivityLimit)
            .ToList();

        var needsReviewApplications = applications.Count(
            application =>
                applicationReviewStates.TryGetValue(application.Id, out var state) &&
                state.RequiresReview);
        var closedApplications = applications.Count(
            application => application.Status is ApplicationStatus.Rejected or ApplicationStatus.Closed);
        var metricsApplications = applications.ToList();

        applications = applications
            .Where(application => MatchesDashboardView(application, options.View, applicationIdsWithOpenReminder, applicationLastOutboundReplyAtUtc, applicationReviewStates))
            .Where(application => MatchesDashboardFilters(application, options))
            .ToList();

        applications = string.Equals(options.Sort, "urgency", StringComparison.OrdinalIgnoreCase)
            ? applications
                .OrderByDescending(application =>
                {
                    nextReminderByApplicationId.TryGetValue(application.Id, out var nextReminder);
                    return CalculateUrgencyScore(
                        application,
                        nextReminder,
                        applicationLastOutboundReplyAtUtc.GetValueOrDefault(application.Id),
                        nowUtc);
                })
                .ThenByDescending(application => application.LastActivityAtUtc)
                .ToList()
            : applications
                .OrderByDescending(application => applicationIdsWithOpenReminder.Contains(application.Id))
                .ThenBy(application => application.Priority)
                .ThenByDescending(application => application.LastActivityAtUtc)
                .ToList();

        var nextCursor = default(string);
        if (options.Limit is > 0)
        {
            var offset = Math.Max(0, options.CursorOffset ?? 0);
            var limitedApplications = applications
                .Skip(offset)
                .Take(options.Limit.Value)
                .ToList();
            nextCursor = applications.Count > offset + limitedApplications.Count
                ? (offset + limitedApplications.Count).ToString(CultureInfo.InvariantCulture)
                : null;
            applications = limitedApplications;
        }

        visibleApplicationIds = applications
            .Select(application => application.Id)
            .ToHashSet();
        pendingActionRecords = pendingActionRecords
            .Where(record => visibleApplicationIds.Contains(record.Reminder.JobApplicationId))
            .ToList();
        events = events
            .Where(@event => visibleApplicationIds.Contains(@event.JobApplicationId))
            .ToList();

        var metrics = new DashboardMetricsDto(
            TotalApplications: metricsApplications.Count,
            ActiveApplications: metricsApplications.Count(application => application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed),
            NeedsAction: visibleActionReminderRecords.Count,
            NeedsReview: needsReviewApplications,
            WaitingOnEmployer: metricsApplications.Count(
                application =>
                    application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed &&
                    applicationLastOutboundReplyAtUtc.ContainsKey(application.Id) &&
                    !applicationIdsWithOpenReminder.Contains(application.Id) &&
                    !ApplicationRequiresReview(application, applicationReviewStates)),
            Interviews: metricsApplications.Count(application => application.Status == ApplicationStatus.Interviewing),
            Offers: metricsApplications.Count(application => application.Status == ApplicationStatus.Offer),
            Rejections: metricsApplications.Count(application => application.Status == ApplicationStatus.Rejected),
            ReviewQueue: inboxOverviewCounts.ReviewQueue,
            ClosedApplications: closedApplications);

        var applicationCards = applications
            .Select(
                application =>
                {
                    applicationReviewStates.TryGetValue(application.Id, out var reviewState);
                    nextReminderByApplicationId.TryGetValue(application.Id, out var nextReminder);
                    var hasOutboundReply = applicationLastOutboundReplyAtUtc.ContainsKey(application.Id);
                    var nextAction = BuildNextAction(application, nextReminder, reviewState, hasOutboundReply);
                    var priority = BuildPriority(application, nextAction, reviewState);
                    var urgencyScore = CalculateUrgencyScore(
                        application,
                        nextReminder,
                        applicationLastOutboundReplyAtUtc.GetValueOrDefault(application.Id),
                        nowUtc);

                    return new ApplicationSummaryDto(
                        application.Id,
                        application.Company,
                        application.Role,
                        ToLabel(application.Status),
                        application.Priority,
                        applicationReceivedTimes.GetValueOrDefault(application.Id, application.CreatedAtUtc),
                        application.LastActivityAtUtc,
                        application.Source,
                        applicationIdsWithOpenReminder.Contains(application.Id),
                        applicationGmailUrls.GetValueOrDefault(application.Id),
                        hasOutboundReply,
                        applicationLastOutboundReplyAtUtc.GetValueOrDefault(application.Id),
                        nextAction,
                        priority.Label,
                        priority.Reason,
                        reviewState?.ParserConfidence,
                        reviewState?.RequiresReview ?? false,
                        reviewState?.ReviewReason,
                        latestEvidenceByApplication.GetValueOrDefault(application.Id),
                        urgencyScore,
                        nextReminder?.Reminder.DueAtUtc);
                })
            .ToList();

        var pendingActions = pendingActionRecords
            .Select(
                record =>
                {
                    var reminder = record.Reminder;
                    var evidenceEmail = record.EvidenceEmail;

                    return new PendingActionDto(
                        reminder.Id,
                        reminder.JobApplicationId,
                        reminder.Company,
                        reminder.Role,
                        reminder.Title,
                        reminder.DueAtUtc,
                        ToLabel(reminder.Type),
                        ToLabel(reminder.Status),
                        evidenceEmail?.Id,
                        evidenceEmail?.ThreadId,
                        evidenceEmail?.ExternalMessageId,
                        evidenceEmail?.DueDateUtc.HasValue == true,
                        evidenceEmail?.GmailUrl ?? applicationGmailUrls.GetValueOrDefault(reminder.JobApplicationId),
                        ResolveTaskType(reminder, evidenceEmail),
                        NormalizeNullable(evidenceEmail?.Subject),
                        NormalizeNullable(evidenceEmail?.Sender),
                        evidenceEmail?.ReceivedAtUtc,
                        evidenceEmail?.DueDateUtc.HasValue == true,
                        ResolveDeadlineHint(evidenceEmail),
                        BuildPendingActionReason(reminder, evidenceEmail, nowUtc));
                })
            .OrderByDescending(action => action.HasExplicitDueDate)
            .ThenBy(action => action.DueAtUtc)
            .Take(6)
            .ToList();

        var recentActivity = events
            .Select(
                @event => new RecentActivityDto(
                    @event.Id,
                    @event.JobApplicationId,
                    @event.Company,
                    @event.Role,
                    BuildActivityDescription(@event.EventType, @event.Company),
                    @event.EventType,
                    @event.InboxEmailReceivedAtUtc ?? @event.EventTimeUtc,
                    @event.EventTimeUtc,
                    GetActivityImportanceScore(@event.EventType)))
            .ToList();

        var primaryInboxAccount = visibleInboxAccounts
            .OrderBy(account => GetAccountPriority(account))
            .ThenByDescending(account => account.LastSyncedAtUtc ?? account.CreatedAtUtc)
            .FirstOrDefault();

        var pendingAnalysisEmails = analysisJobs.Count(job => job.Status == EmailProcessingStatus.Pending);
        var processingAnalysisEmails = analysisJobs.Count(job => job.Status == EmailProcessingStatus.Processing);
        var failedAnalysisEmails = analysisJobs.Count(job => job.Status == EmailProcessingStatus.Failed);
        var recognizedApplications = primaryInboxAccount is null
            ? 0
            : metricsApplications.Count(application => application.CreatedAtUtc >= primaryInboxAccount.CreatedAtUtc);
        var backfillStatus = ResolveBackfillStatus(primaryInboxAccount, pendingAnalysisEmails, processingAnalysisEmails);
        var primaryInboxMetadata = primaryInboxAccount is null ? null : ReadInboxAccountMetadata(primaryInboxAccount);
        var totalEstimate = primaryInboxMetadata?.GmailMessagesTotal ?? inboxOverviewCounts.SyncedEmails;

        var inboxOverview = new InboxOverviewDto(
            primaryInboxAccount?.EmailAddress ?? resolvedUserEmail,
            inboxOverviewCounts.SyncedEmails,
            inboxOverviewCounts.ProcessedEmails,
            visibleInboxAccounts.MaxBy(account => account.LastSyncedAtUtc)?.LastSyncedAtUtc,
            pendingAnalysisEmails,
            processingAnalysisEmails,
            failedAnalysisEmails,
            analysisJobs
                .Where(job => job.CompletedAtUtc.HasValue)
                .Select(job => job.CompletedAtUtc)
                .DefaultIfEmpty()
                .Max(),
            analysisJobs
                .Where(job => !string.IsNullOrWhiteSpace(job.LastError))
                .OrderByDescending(job => job.UpdatedAtUtc)
                .Select(job => job.LastError)
                .FirstOrDefault(),
            inboxOverviewCounts.SyncedEmails,
            totalEstimate,
            recognizedApplications,
            backfillStatus);

        return new DashboardResponse(metrics, inboxOverview, applicationCards, pendingActions, recentActivity, nextCursor);
    }

    private static ApplicationReviewState BuildApplicationReviewState(IReadOnlyList<DashboardEmailRecord> emails)
    {
        var parserConfidence = emails
            .Where(email => email.ClassificationConfidence.HasValue)
            .Select(email => email.ClassificationConfidence!.Value)
            .DefaultIfEmpty()
            .Min();
        var hasParserConfidence = emails.Any(email => email.ClassificationConfidence.HasValue);
        var requiresReview = emails.Any(email => email.RequiresManualReview);
        var reviewReason = emails
            .Where(email => email.RequiresManualReview && !string.IsNullOrWhiteSpace(email.ReviewReason))
            .OrderByDescending(email => email.ReceivedAtUtc)
            .Select(email => email.ReviewReason)
            .FirstOrDefault();

        return new ApplicationReviewState(
            hasParserConfidence ? parserConfidence : null,
            requiresReview,
            requiresReview
                ? reviewReason ?? "Linked email requires manual review."
                : null);
    }

    private static ApplicationSummaryNextActionDto BuildNextAction(
        JobApplication application,
        DashboardPendingActionRecord? nextReminder,
        ApplicationReviewState? reviewState,
        bool hasOutboundReply)
    {
        if (reviewState?.RequiresReview == true &&
            application.Status is not ApplicationStatus.Offer and not ApplicationStatus.Interviewing)
        {
            return new ApplicationSummaryNextActionDto(
                "Fix company / role in review",
                null,
                false,
                "review",
                null);
        }

        if (nextReminder is not null)
        {
            var hasExplicitDueDate = nextReminder.EvidenceEmail?.DueDateUtc.HasValue == true;
            return new ApplicationSummaryNextActionDto(
                nextReminder.Reminder.Title,
                hasExplicitDueDate ? nextReminder.Reminder.DueAtUtc : null,
                hasExplicitDueDate,
                "action",
                nextReminder.Reminder.Id);
        }

        return application.Status switch
        {
            ApplicationStatus.OaPending => new ApplicationSummaryNextActionDto(
                "Complete assessment",
                null,
                false,
                "action",
                null),
            ApplicationStatus.Interviewing => new ApplicationSummaryNextActionDto(
                "Prepare interview",
                null,
                false,
                "action",
                null),
            ApplicationStatus.Offer => new ApplicationSummaryNextActionDto(
                "Reply to offer",
                null,
                false,
                "action",
                null),
            ApplicationStatus.RecruiterContact => new ApplicationSummaryNextActionDto(
                "Reply to recruiter",
                null,
                false,
                "action",
                null),
            ApplicationStatus.Applied when hasOutboundReply => new ApplicationSummaryNextActionDto(
                "Wait for response",
                null,
                false,
                "waiting",
                null),
            ApplicationStatus.Applied => new ApplicationSummaryNextActionDto(
                "Confirm application sent",
                null,
                false,
                "muted",
                null),
            ApplicationStatus.Rejected or ApplicationStatus.Closed => new ApplicationSummaryNextActionDto(
                "Archive or dispute",
                null,
                false,
                "muted",
                null),
            _ => new ApplicationSummaryNextActionDto(
                "Review application",
                null,
                false,
                "muted",
                null)
        };
    }

    private PrioritySummary BuildPriority(
        JobApplication application,
        ApplicationSummaryNextActionDto nextAction,
        ApplicationReviewState? reviewState)
    {
        if (nextAction.Tone == "review")
        {
            return new PrioritySummary(
                "High",
                reviewState?.ReviewReason ?? "Linked evidence needs manual review.");
        }

        if (nextAction.SourceReminderId.HasValue)
        {
            if (nextAction.DueAtUtc.HasValue)
            {
                var dueIn = nextAction.DueAtUtc.Value - _clock.UtcNow;
                if (dueIn <= TimeSpan.Zero)
                {
                    return new PrioritySummary("Urgent", "Open reminder is overdue.");
                }

                if (dueIn <= TimeSpan.FromDays(2))
                {
                    return new PrioritySummary("Urgent", "Open reminder is due within 2 days.");
                }

                return new PrioritySummary("High", "Open reminder has an explicit due date.");
            }

            return new PrioritySummary("High", "Open reminder is active.");
        }

        if (nextAction.Tone == "action")
        {
            return new PrioritySummary(
                application.Status is ApplicationStatus.Offer or ApplicationStatus.Interviewing or ApplicationStatus.OaPending
                    ? "High"
                    : "Medium",
                "Application stage requires a concrete next action.");
        }

        if (nextAction.Tone == "waiting")
        {
            return new PrioritySummary("Medium", "Outbound reply sent; waiting on employer.");
        }

        if (application.Status is ApplicationStatus.Rejected or ApplicationStatus.Closed)
        {
            return new PrioritySummary("Background", "Terminal application state.");
        }

        return new PrioritySummary("Low", "No deadline and no open task.");
    }

    private static ApplicationSummaryEvidenceDto BuildLatestEvidence(
        DashboardEventRecord @event,
        DashboardEmailRecord email)
    {
        return new ApplicationSummaryEvidenceDto(
            email.Id,
            BuildEvidenceSummary(@event.EventType),
            null,
            null,
            null,
            @event.EventTimeUtc,
            @event.EventType,
            email.GmailUrl);
    }

    private static string BuildEvidenceSummary(string eventType)
    {
        return eventType switch
        {
            "recruiter_outreach" => "Agent classified the latest evidence as recruiter outreach.",
            "application_received" => "Agent classified the latest evidence as an application receipt.",
            "oa_invitation" => "Agent classified the latest evidence as an assessment or questionnaire request.",
            "interview_invitation" => "Agent classified the latest evidence as an interview invitation.",
            "interview_confirmation" => "Agent classified the latest evidence as an interview confirmation.",
            "generic_update" => "Agent classified the latest evidence as a recruiting update.",
            "rejection" => "Agent classified the latest evidence as a rejection decision.",
            "offer" => "Agent classified the latest evidence as an offer-stage update.",
            "gmail_reply_detected" => "Agent detected an outbound reply in the linked thread.",
            "manual_stage_update" => "Agent surfaced a manual stage correction as the latest evidence.",
            _ => "Agent classified the latest linked evidence for this application."
        };
    }

    private static string? NormalizeNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(' ', value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsDecisionEvidenceEvent(string eventType)
    {
        return eventType is
            "recruiter_outreach" or
            "application_received" or
            "oa_invitation" or
            "interview_invitation" or
            "interview_confirmation" or
            "generic_update" or
            "rejection" or
            "offer" or
            "gmail_reply_detected" or
            "manual_stage_update";
    }

    private static string ResolveTaskType(DashboardReminderRecord reminder, DashboardEmailRecord? evidenceEmail)
    {
        if (evidenceEmail?.Category == EmailCategory.OaInvitation ||
            reminder.Type == ReminderType.AssessmentDeadline)
        {
            return "assessment";
        }

        if (evidenceEmail?.Category is EmailCategory.InterviewInvitation or EmailCategory.InterviewConfirmation ||
            reminder.Type == ReminderType.InterviewPreparation)
        {
            return "interview";
        }

        if (evidenceEmail?.Category == EmailCategory.Offer)
        {
            return "offer";
        }

        if (evidenceEmail?.Category == EmailCategory.RecruiterOutreach)
        {
            return "reply";
        }

        if (reminder.Type == ReminderType.FollowUp)
        {
            return "follow_up";
        }

        return "other";
    }

    private static string? ResolveDeadlineHint(DashboardEmailRecord? evidenceEmail)
    {
        return evidenceEmail?.DueDateUtc.HasValue == true
            ? null
            : "No explicit deadline was found in the source email.";
    }

    private static string BuildPendingActionReason(
        DashboardReminderRecord reminder,
        DashboardEmailRecord? evidenceEmail,
        DateTimeOffset nowUtc)
    {
        if (evidenceEmail?.DueDateUtc.HasValue == true)
        {
            return $"Explicit deadline found in email due {evidenceEmail.DueDateUtc.Value:MMM d}.";
        }

        var daysUntilDue = Math.Ceiling((reminder.DueAtUtc - nowUtc).TotalDays);
        var timing = daysUntilDue < 0
            ? "Reminder is overdue."
            : daysUntilDue <= 1
                ? "Reminder is due within 24 hours."
                : $"Reminder is due in {daysUntilDue:0} days.";

        return reminder.Type switch
        {
            ReminderType.AssessmentDeadline => $"{timing} Assessment-related thread needs action.",
            ReminderType.InterviewPreparation => $"{timing} Interview thread needs preparation.",
            ReminderType.FollowUp => $"{timing} No newer reply has cleared this follow-up.",
            _ => timing
        };
    }

    private static int CalculateUrgencyScore(
        JobApplication application,
        DashboardPendingActionRecord? nextReminder,
        DateTimeOffset? lastOutboundReplyAtUtc,
        DateTimeOffset nowUtc)
    {
        var score = 0;
        if (nextReminder is not null)
        {
            var dueIn = nextReminder.Reminder.DueAtUtc - nowUtc;
            score += dueIn <= TimeSpan.FromHours(24)
                ? 100
                : dueIn <= TimeSpan.FromHours(72)
                    ? 60
                    : 0;
        }

        if (application.Status is ApplicationStatus.OaPending or ApplicationStatus.Interviewing or ApplicationStatus.Offer)
        {
            score += 40;
        }

        if (application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed &&
            (!lastOutboundReplyAtUtc.HasValue || lastOutboundReplyAtUtc.Value <= nowUtc.AddDays(-7)))
        {
            score += 20;
        }

        if (application.Status is ApplicationStatus.Rejected or ApplicationStatus.Closed)
        {
            score -= 10;
        }

        return Math.Clamp(score, 0, 200);
    }

    private static string ResolveBackfillStatus(
        DashboardInboxAccountRecord? primaryInboxAccount,
        int pendingAnalysisEmails,
        int processingAnalysisEmails)
    {
        if (primaryInboxAccount is null)
        {
            return "idle";
        }

        if (!primaryInboxAccount.LastSyncedAtUtc.HasValue ||
            pendingAnalysisEmails > 0 ||
            processingAnalysisEmails > 0)
        {
            return "running";
        }

        return "complete";
    }

    private static string BuildActivityDescription(string eventType, string company)
    {
        return eventType switch
        {
            "recruiter_outreach" => $"Recruiter outreach received from {company}",
            "application_received" => $"Application receipt received from {company}",
            "oa_invitation" => $"Assessment or questionnaire request detected for {company}",
            "interview_invitation" => $"Interview invitation received from {company}",
            "interview_confirmation" => $"Interview confirmation received from {company}",
            "generic_update" => $"Recruiting update received from {company}",
            "rejection" => $"Rejection received from {company}",
            "offer" => $"Offer received from {company}",
            "reminder_completed" => $"Reminder completed for {company}",
            "reminder_dismissed" => $"Reminder dismissed for {company}",
            "reminder_reopened" => $"Reminder reopened for {company}",
            "reminder_snoozed" => $"Reminder snoozed for {company}",
            "gmail_reply_detected" => $"Sent Gmail reply detected for {company}",
            "manual_stage_update" => $"Application status updated manually for {company}",
            _ => $"Inbox activity updated for {company}"
        };
    }

    private static bool MatchesDashboardView(
        JobApplication application,
        string? view,
        IReadOnlySet<Guid> applicationIdsWithOpenReminder,
        IReadOnlyDictionary<Guid, DateTimeOffset?> applicationLastOutboundReplyAtUtc,
        IReadOnlyDictionary<Guid, ApplicationReviewState> applicationReviewStates)
    {
        return NormalizeView(view) switch
        {
            "needs-action" =>
                applicationIdsWithOpenReminder.Contains(application.Id) &&
                application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed,
            "needs-review" =>
                applicationReviewStates.TryGetValue(application.Id, out var state) &&
                state.RequiresReview,
            "active" => application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed,
            "waiting" =>
                application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed &&
                applicationLastOutboundReplyAtUtc.ContainsKey(application.Id) &&
                !applicationIdsWithOpenReminder.Contains(application.Id) &&
                !ApplicationRequiresReview(application, applicationReviewStates),
            "interviewing" => application.Status == ApplicationStatus.Interviewing,
            "offer" => application.Status == ApplicationStatus.Offer,
            "rejected" => application.Status == ApplicationStatus.Rejected,
            _ => true
        };
    }

    private static bool ApplicationRequiresReview(
        JobApplication application,
        IReadOnlyDictionary<Guid, ApplicationReviewState> applicationReviewStates)
    {
        return applicationReviewStates.TryGetValue(application.Id, out var state) &&
               state.RequiresReview;
    }

    private static bool TryBuildIdentityReviewState(
        JobApplication application,
        out ApplicationReviewState reviewState)
    {
        var reason = ResolveIdentityReviewReason(application);
        if (reason is null)
        {
            reviewState = default!;
            return false;
        }

        reviewState = new ApplicationReviewState(0.35m, true, reason);
        return true;
    }

    private static string? ResolveIdentityReviewReason(JobApplication application)
    {
        var companyLooksLikePerson = CompanyNameNormalizer.IsLikelyPersonName(application.Company);
        var companyLooksLikeClearContactName = application.Company.Contains(',', StringComparison.Ordinal);
        var roleLooksUnusable = IsSuspiciousRoleToken(application.Role);

        if ((companyLooksLikeClearContactName || companyLooksLikePerson) && roleLooksUnusable)
        {
            return "Company and role look like parser errors.";
        }

        if (companyLooksLikeClearContactName && companyLooksLikePerson)
        {
            return "Company looks like a recruiter or contact name.";
        }

        if (roleLooksUnusable)
        {
            return "Role could not be extracted confidently.";
        }

        return null;
    }

    private static bool IsSuspiciousRoleToken(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var normalized = role.Trim().ToLowerInvariant();
        return normalized is "you" or "your" or "me" or "candidate" or "applicant";
    }

    private static ApplicationReviewState MergeApplicationReviewState(
        ApplicationReviewState? existing,
        ApplicationReviewState next)
    {
        if (existing is null)
        {
            return next;
        }

        return new ApplicationReviewState(
            MinNullable(existing.ParserConfidence, next.ParserConfidence),
            existing.RequiresReview || next.RequiresReview,
            existing.RequiresReview && !string.IsNullOrWhiteSpace(existing.ReviewReason)
                ? existing.ReviewReason
                : next.ReviewReason);
    }

    private static decimal? MinNullable(decimal? left, decimal? right)
    {
        return (left, right) switch
        {
            (null, null) => null,
            (null, _) => right,
            (_, null) => left,
            _ => Math.Min(left.Value, right.Value)
        };
    }

    private static bool MatchesDashboardFilters(JobApplication application, DashboardQueryOptions options)
    {
        if (!ContainsIfSet(application.Company, options.Company))
        {
            return false;
        }

        if (!ContainsIfSet(application.RoleCanonical ?? application.Role, options.RoleCanonical))
        {
            return false;
        }

        if (!ContainsIfSet(application.SourceSystem ?? application.Source, options.Source))
        {
            return false;
        }

        if (!ContainsIfSet(application.Location ?? string.Empty, options.Location))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.Query) &&
            !ContainsIfSet(application.Company, options.Query) &&
            !ContainsIfSet(application.Role, options.Query) &&
            !ContainsIfSet(ToLabel(application.Status), options.Query))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsIfSet(string value, string? filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
               value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeView(string? view)
    {
        return view?.Trim().ToLowerInvariant() switch
        {
            "needs-action" => "needs-action",
            "needs-review" => "needs-review",
            "active" => "active",
            "waiting" => "waiting",
            "interviewing" => "interviewing",
            "offer" => "offer",
            "rejected" => "rejected",
            "all" => "all",
            _ => "all"
        };
    }

    private static int GetActivityImportanceScore(string eventType)
    {
        return eventType switch
        {
            "offer" => 100,
            "interview_invitation" or "interview_confirmation" => 90,
            "oa_invitation" => 80,
            "recruiter_outreach" => 70,
            "gmail_reply_detected" => 60,
            "application_received" => 50,
            "generic_update" => 40,
            "manual_stage_update" => 35,
            "rejection" => 20,
            var value when value.StartsWith("reminder_", StringComparison.OrdinalIgnoreCase) => 5,
            _ => 30
        };
    }

    private static string ToLabel<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        return string.Concat(
            name.Select(
                (character, index) =>
                    index > 0 && char.IsUpper(character)
                        ? $" {character}"
                        : character.ToString()));
    }

    private static DashboardEmailRecord? ResolveReminderEvidence(
        DashboardReminderRecord reminder,
        IReadOnlyList<DashboardEmailRecord> emails)
    {
        if (emails.Count == 0)
        {
            return null;
        }

        EmailCategory[] preferredCategories = reminder.Type switch
        {
            ReminderType.AssessmentDeadline => [EmailCategory.OaInvitation],
            ReminderType.InterviewPreparation => [EmailCategory.InterviewConfirmation, EmailCategory.InterviewInvitation],
            ReminderType.FollowUp => [EmailCategory.RecruiterOutreach],
            _ => []
        };

        var actionableCandidates = emails
            .Where(email => email.ActionRequired)
            .ToList();
        var categoryCandidates = actionableCandidates
            .Where(email => preferredCategories.Length == 0 || (email.Category.HasValue && preferredCategories.Contains(email.Category.Value)))
            .ToList();

        var rankedCandidates = (categoryCandidates.Count > 0 ? categoryCandidates : actionableCandidates)
            .OrderBy(email => ScoreReminderEvidence(reminder, email))
            .ThenByDescending(email => email.DueDateUtc.HasValue)
            .ThenByDescending(email => email.ReceivedAtUtc)
            .ToList();

        if (rankedCandidates.Count > 0)
        {
            return rankedCandidates[0];
        }

        return emails
            .OrderByDescending(email => email.ReceivedAtUtc)
            .FirstOrDefault();
    }

    private static long ScoreReminderEvidence(DashboardReminderRecord reminder, DashboardEmailRecord email)
    {
        if (!email.DueDateUtc.HasValue)
        {
            return long.MaxValue / 4;
        }

        return Math.Abs((email.DueDateUtc.Value - reminder.DueAtUtc).Ticks);
    }

    private static bool IsLowSignalFallbackReminder(DashboardReminderRecord reminder)
    {
        return reminder.Type == ReminderType.FollowUp &&
               reminder.Title.TrimStart().StartsWith(
                   "Review next step for ",
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DashboardUserRecord(
        Guid Id,
        string Email);

    private sealed record DashboardInboxAccountRecord(
        Guid Id,
        string Provider,
        string EmailAddress,
        string? OAuthMetadataJson,
        DateTimeOffset? LastSyncedAtUtc,
        DateTimeOffset CreatedAtUtc);

    private sealed record DashboardEmailRecord(
        Guid Id,
        Guid InboxAccountId,
        Guid? JobApplicationId,
        string Provider,
        string AccountEmailAddress,
        string ThreadId,
        string ExternalMessageId,
        string Subject,
        string Sender,
        DateTimeOffset ReceivedAtUtc,
        DateTimeOffset? ProcessedAtUtc,
        DateTimeOffset? DueDateUtc,
        EmailCategory? Category,
        bool ActionRequired,
        bool RequiresManualReview,
        string? ReviewReason,
        decimal? ClassificationConfidence,
        bool IsOutbound)
    {
        public string? GmailUrl => InboxEmailLinkBuilder.BuildGmailUrl(
            Provider,
            AccountEmailAddress,
            ThreadId,
            ExternalMessageId);
    }

    private sealed record DashboardApplicationEmailSummaryRecord(
        Guid JobApplicationId,
        DateTimeOffset FirstReceivedAtUtc,
        string? LatestProvider,
        string? LatestAccountEmailAddress,
        string? LatestThreadId,
        string? LatestExternalMessageId)
    {
        public string? GmailUrl => string.IsNullOrWhiteSpace(LatestProvider) ||
            string.IsNullOrWhiteSpace(LatestAccountEmailAddress) ||
            string.IsNullOrWhiteSpace(LatestThreadId) ||
            string.IsNullOrWhiteSpace(LatestExternalMessageId)
                ? null
                : InboxEmailLinkBuilder.BuildGmailUrl(
                    LatestProvider,
                    LatestAccountEmailAddress,
                    LatestThreadId,
                    LatestExternalMessageId);
    }

    private sealed record DashboardAnalysisJobRecord(
        EmailProcessingStatus Status,
        DateTimeOffset? CompletedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string? LastError);

    private sealed record DashboardInboxOverviewCounts(
        int SyncedEmails,
        int ProcessedEmails,
        int ReviewQueue);

    private sealed record DashboardPendingActionRecord(
        DashboardReminderRecord Reminder,
        DashboardEmailRecord? EvidenceEmail);

    private sealed record DashboardReminderRecord(
        Guid Id,
        Guid JobApplicationId,
        string Company,
        string Role,
        ReminderType Type,
        string Title,
        DateTimeOffset DueAtUtc,
        ReminderStatus Status);

    private sealed record DashboardEventRecord(
        Guid Id,
        Guid JobApplicationId,
        Guid? InboxEmailId,
        string EventType,
        DateTimeOffset EventTimeUtc,
        string Company,
        string Role,
        DateTimeOffset? InboxEmailReceivedAtUtc);

    private sealed record ApplicationReviewState(
        decimal? ParserConfidence,
        bool RequiresReview,
        string? ReviewReason);

    private sealed record PrioritySummary(
        string Label,
        string Reason);

    private static bool IsWorkflowEmail(DashboardEmailRecord email)
    {
        if (!email.ProcessedAtUtc.HasValue)
        {
            return true;
        }

        return email.JobApplicationId.HasValue ||
               email.Category.HasValue ||
               email.ActionRequired ||
               email.RequiresManualReview;
    }

    private static int GetAccountPriority(DashboardInboxAccountRecord account)
    {
        var metadata = ReadInboxAccountMetadata(account);

        return metadata.ConnectionStatus switch
        {
            "Connected" => 0,
            "DNS verified" => 0,
            "Waiting for DNS" => 1,
            "Waiting for Google" => 2,
            _ => 2
        };
    }

    private static InboxAccountMetadata ReadInboxAccountMetadata(DashboardInboxAccountRecord account)
    {
        if (!string.IsNullOrWhiteSpace(account.OAuthMetadataJson))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<InboxAccountMetadata>(
                    account.OAuthMetadataJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (metadata is not null)
                {
                    return metadata;
                }
            }
            catch (JsonException)
            {
                return new InboxAccountMetadata
                {
                    SetupMode = "error",
                    ConnectionStatus = "Metadata invalid",
                    NextStep = "Reconnect this inbox. Stored connection metadata could not be parsed.",
                    IsValid = false,
                    InvalidReason = "json_parse"
                };
            }
        }

        return new InboxAccountMetadata
        {
            SetupMode = "manual",
            ConnectionStatus = "Configured",
            NextStep = $"Connection details are stored locally for {account.EmailAddress}."
        };
    }

    private static string ResolveUserEmail(string? userEmail)
    {
        return ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
    }
}

public sealed record DashboardQueryOptions(
    string? View = null,
    int? Limit = null,
    int? CursorOffset = null,
    string? Query = null,
    string? Company = null,
    string? RoleCanonical = null,
    string? Source = null,
    string? Location = null,
    string? Sort = null)
{
    public static DashboardQueryOptions Empty { get; } = new();
}
