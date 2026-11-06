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

// Turns the loaded rows into the dashboard's DTOs: next actions, priorities, evidence, and labels.
public sealed partial class DashboardQueryService
{
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

    private static Dictionary<Guid, ApplicationSummaryEvidenceDto> BuildLatestEvidenceByApplication(
        IEnumerable<DashboardEventRecord> events,
        IEnumerable<DashboardEmailRecord> emails)
    {
        var emailRecordsById = emails.ToDictionary(email => email.Id);
        return events
            .Where(@event => IsDecisionEvidenceEvent(@event.EventType) && @event.InboxEmailId.HasValue)
            .Select(
                @event => emailRecordsById.TryGetValue(@event.InboxEmailId!.Value, out var email)
                    ? new { @event.JobApplicationId, Evidence = BuildLatestEvidence(@event, email) }
                    : null)
            .Where(record => record is not null)
            .GroupBy(record => record!.JobApplicationId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(record => record!.Evidence.OccurredAtUtc).First()!.Evidence);
    }

    private static DashboardMetricsDto BuildMetrics(
        IReadOnlyCollection<JobApplication> applications,
        int needsAction,
        IReadOnlyDictionary<Guid, ApplicationReviewState> reviewStates,
        IReadOnlyDictionary<Guid, DateTimeOffset?> lastOutboundReplyAtUtc,
        IReadOnlySet<Guid> applicationIdsWithOpenReminder,
        DashboardInboxOverviewCounts inboxOverviewCounts)
    {
        return new DashboardMetricsDto(
            TotalApplications: applications.Count,
            ActiveApplications: applications.Count(application => application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed),
            NeedsAction: needsAction,
            NeedsReview: applications.Count(application => ApplicationRequiresReview(application, reviewStates)),
            WaitingOnEmployer: applications.Count(
                application =>
                    application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed &&
                    lastOutboundReplyAtUtc.ContainsKey(application.Id) &&
                    !applicationIdsWithOpenReminder.Contains(application.Id) &&
                    !ApplicationRequiresReview(application, reviewStates)),
            Interviews: applications.Count(application => application.Status == ApplicationStatus.Interviewing),
            Offers: applications.Count(application => application.Status == ApplicationStatus.Offer),
            Rejections: applications.Count(application => application.Status == ApplicationStatus.Rejected),
            ReviewQueue: inboxOverviewCounts.ReviewQueue,
            ClosedApplications: applications.Count(application => application.Status is ApplicationStatus.Rejected or ApplicationStatus.Closed));
    }

    private List<ApplicationSummaryDto> BuildApplicationCards(
        IEnumerable<JobApplication> applications,
        IReadOnlyDictionary<Guid, ApplicationReviewState> reviewStates,
        IReadOnlyDictionary<Guid, DashboardPendingActionRecord> nextReminderByApplicationId,
        IReadOnlyDictionary<Guid, DateTimeOffset?> lastOutboundReplyAtUtc,
        IReadOnlySet<Guid> applicationIdsWithOpenReminder,
        IReadOnlyDictionary<Guid, DateTimeOffset> receivedTimes,
        IReadOnlyDictionary<Guid, string?> gmailUrls,
        IReadOnlyDictionary<Guid, ApplicationSummaryEvidenceDto> latestEvidenceByApplication,
        DateTimeOffset nowUtc)
    {
        return applications
            .Select(
                application =>
                {
                    reviewStates.TryGetValue(application.Id, out var reviewState);
                    nextReminderByApplicationId.TryGetValue(application.Id, out var nextReminder);
                    var hasOutboundReply = lastOutboundReplyAtUtc.ContainsKey(application.Id);
                    var nextAction = BuildNextAction(application, nextReminder, reviewState, hasOutboundReply);
                    var priority = BuildPriority(application, nextAction, reviewState);
                    var urgencyScore = CalculateUrgencyScore(
                        application,
                        nextReminder,
                        lastOutboundReplyAtUtc.GetValueOrDefault(application.Id),
                        nowUtc);

                    return new ApplicationSummaryDto(
                        application.Id,
                        application.Company,
                        application.Role,
                        ToLabel(application.Status),
                        application.Priority,
                        receivedTimes.GetValueOrDefault(application.Id, application.CreatedAtUtc),
                        application.LastActivityAtUtc,
                        application.Source,
                        applicationIdsWithOpenReminder.Contains(application.Id),
                        gmailUrls.GetValueOrDefault(application.Id),
                        hasOutboundReply,
                        lastOutboundReplyAtUtc.GetValueOrDefault(application.Id),
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
    }

    private static List<PendingActionDto> BuildPendingActions(
        IEnumerable<DashboardPendingActionRecord> records,
        IReadOnlyDictionary<Guid, string?> gmailUrls,
        DateTimeOffset nowUtc)
    {
        return records
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
                        evidenceEmail?.GmailUrl ?? gmailUrls.GetValueOrDefault(reminder.JobApplicationId),
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
    }

    private static List<RecentActivityDto> BuildRecentActivity(IEnumerable<DashboardEventRecord> events)
    {
        return events
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
    }

    private static InboxOverviewDto BuildInboxOverview(
        DashboardData data,
        IReadOnlyCollection<JobApplication> applications,
        string userEmail)
    {
        var primaryInboxAccount = data.VisibleInboxAccounts
            .OrderBy(account => GetAccountPriority(account))
            .ThenByDescending(account => account.LastSyncedAtUtc ?? account.CreatedAtUtc)
            .FirstOrDefault();
        var analysisJobs = data.AnalysisJobs;
        var pendingAnalysisEmails = analysisJobs.Count(job => job.Status == EmailProcessingStatus.Pending);
        var processingAnalysisEmails = analysisJobs.Count(job => job.Status == EmailProcessingStatus.Processing);
        var failedAnalysisEmails = analysisJobs.Count(job => job.Status == EmailProcessingStatus.Failed);
        var recognizedApplications = primaryInboxAccount is null
            ? 0
            : applications.Count(application => application.CreatedAtUtc >= primaryInboxAccount.CreatedAtUtc);
        var primaryInboxMetadata = primaryInboxAccount is null ? null : ReadInboxAccountMetadata(primaryInboxAccount);
        var counts = data.InboxOverviewCounts;

        return new InboxOverviewDto(
            primaryInboxAccount?.EmailAddress ?? userEmail,
            counts.SyncedEmails,
            counts.ProcessedEmails,
            data.VisibleInboxAccounts.MaxBy(account => account.LastSyncedAtUtc)?.LastSyncedAtUtc,
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
            counts.SyncedEmails,
            primaryInboxMetadata?.GmailMessagesTotal ?? counts.SyncedEmails,
            recognizedApplications,
            ResolveBackfillStatus(primaryInboxAccount, pendingAnalysisEmails, processingAnalysisEmails));
    }
}
