using System.Globalization;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Agents;
using Careeroc.Contracts.Applications;
using Careeroc.Contracts.Review;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class ApplicationDetailQueryService
{
    private const int LinkedEmailFetchLimit = 250;
    private const int EventFetchLimit = 100;
    private const int ReminderFetchLimit = 50;
    private const int ArtifactEvidenceFetchLimit = 50;

    private static readonly ApplicationStatus[] PipelineStatuses =
    [
        ApplicationStatus.Applied,
        ApplicationStatus.RecruiterContact,
        ApplicationStatus.OaPending,
        ApplicationStatus.Interviewing,
        ApplicationStatus.Offer
    ];

    private readonly ICareerocDbContext _dbContext;
    private readonly UserMemoryProposalService _userMemoryProposalService;

    public ApplicationDetailQueryService(ICareerocDbContext dbContext, UserMemoryProposalService userMemoryProposalService)
    {
        _dbContext = dbContext;
        _userMemoryProposalService = userMemoryProposalService;
    }

    public async Task<ApplicationDetailDto?> GetAsync(
        Guid jobApplicationId,
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var visibleInboxAccountIds = await _dbContext.InboxAccounts
            .AsNoTracking()
            .Where(account => account.UserId == user.Id)
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);

        var application = await _dbContext.JobApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == jobApplicationId && candidate.UserId == user.Id,
                cancellationToken);

        if (application is null)
        {
            return null;
        }

        var emails = await _dbContext.InboxEmails
            .AsNoTracking()
            .Where(
                email =>
                    email.JobApplicationId == application.Id &&
                    visibleInboxAccountIds.Contains(email.InboxAccountId))
            .OrderByDescending(email => email.ReceivedAtUtc)
            .Take(LinkedEmailFetchLimit)
            .Select(
                email => new ApplicationDetailEmailRecord(
                    email.Id,
                    email.InboxAccount.Provider,
                    email.InboxAccount.EmailAddress,
                    email.ExternalMessageId,
                    email.ThreadId,
                    email.Sender,
                    email.Subject,
                    email.ParsedBody,
                    email.RawBody,
                    email.IsOutbound,
                    email.Category,
                    email.ContactName,
                    email.ContactEmail,
                    email.ContactRoleHint,
                    email.ContactType,
                    email.ActionRequired,
                    email.DueDateUtc,
                    email.ClassificationConfidence,
                    email.MatchingConfidence,
                    email.RequiresManualReview,
                    email.ReviewReason,
                    email.ReviewedAtUtc,
                    email.ProcessingNotes,
                    email.ProcessingTraceJson,
                    email.ProcessedAtUtc,
                    email.ReceivedAtUtc))
            .ToListAsync(cancellationToken);

        var effectiveStatus = ResolveEffectiveStatus(application, emails);
        var terminal = ApplicationWorkflowService.IsTerminalStatus(effectiveStatus);
        var reminders = terminal
            ? new List<Reminder>()
            : await _dbContext.Reminders
                .AsNoTracking()
                .Where(
                    reminder =>
                        reminder.JobApplicationId == application.Id &&
                        reminder.Status == ReminderStatus.Open &&
                        !(
                            reminder.Type == ReminderType.FollowUp &&
                            reminder.Title.StartsWith("Review next step for ")))
                .OrderBy(reminder => reminder.DueAtUtc)
                .Take(ReminderFetchLimit)
                .ToListAsync(cancellationToken);

        var eventQuery = _dbContext.JobApplicationEvents
            .AsNoTracking()
            .Where(
                @event =>
                    @event.JobApplicationId == application.Id &&
                    (@event.InboxEmailId == null ||
                     (@event.InboxEmail != null &&
                      visibleInboxAccountIds.Contains(@event.InboxEmail.InboxAccountId))));
        var eventTotalCount = await eventQuery.CountAsync(cancellationToken);
        var events = await eventQuery
            .OrderByDescending(@event => @event.EventTimeUtc)
            .Take(EventFetchLimit)
            .Select(
                @event => new ApplicationDetailEventRecord(
                    @event.Id,
                    @event.EventType,
                    @event.EventTimeUtc))
            .ToListAsync(cancellationToken);

        var artifactEvidenceQuery = _dbContext.ApplicationArtifactEvidence
            .AsNoTracking()
            .Where(evidence => evidence.JobApplicationId == application.Id && evidence.UserId == user.Id);
        var artifactEvidenceTotalCount = await artifactEvidenceQuery.CountAsync(cancellationToken);
        var artifactEvidence = await artifactEvidenceQuery
            .OrderByDescending(evidence => evidence.CreatedAtUtc)
            .Take(ArtifactEvidenceFetchLimit)
            .ToListAsync(cancellationToken);
        var memoryProposals = await _userMemoryProposalService.GetProposalsForApplicationAsync(
            application.Id,
            resolvedUserEmail,
            cancellationToken: cancellationToken);

        var recentActivity = events
            .Take(8)
            .Select(
                @event => new ApplicationDetailActivityDto(
                    @event.Id,
                    BuildActivityDescription(@event.EventType, application.Company),
                    @event.EventType,
                    @event.EventTimeUtc))
            .ToList();
        var hasThreadActionRequired = emails.Any(
            email =>
                email.ActionRequired &&
                email.Category.HasValue &&
                ApplicationWorkflowService.IsActiveWorkflowCategory(email.Category.Value));

        return new ApplicationDetailDto(
            application.Id,
            application.Company,
            application.Role,
            ToLabel(effectiveStatus),
            BuildCompletedStages(application, effectiveStatus, emails, events),
            application.Priority,
            application.Source,
            application.CreatedAtUtc,
            application.LastActivityAtUtc,
            !terminal && (reminders.Count > 0 || hasThreadActionRequired),
            BuildAgentBrief(application, effectiveStatus, reminders, emails, recentActivity),
            BuildContacts(emails),
            reminders
                .Select(
                    reminder =>
                    {
                        var evidenceEmail = ResolveReminderEvidence(reminder, emails);

                        return new ApplicationDetailReminderDto(
                            reminder.Id,
                            reminder.Title,
                            reminder.DueAtUtc,
                            ToLabel(reminder.Type),
                            ToLabel(reminder.Status),
                            evidenceEmail?.Id,
                            evidenceEmail?.ThreadId,
                            evidenceEmail?.ExternalMessageId,
                            evidenceEmail?.DueDateUtc.HasValue == true,
                            evidenceEmail is null ? null : BuildGmailUrl(evidenceEmail));
                    })
                .ToList(),
            recentActivity,
            BuildThreads(emails, suppressActions: terminal),
            BuildFeedbackNotes(emails),
            BuildArtifactEvidence(artifactEvidence),
            eventTotalCount,
            artifactEvidenceTotalCount,
            memoryProposals,
            BuildApplicationAgentTrace(application, effectiveStatus, reminders, emails, recentActivity));
    }

    private static ApplicationAgentBriefDto BuildAgentBrief(
        JobApplication application,
        ApplicationStatus effectiveStatus,
        IReadOnlyList<Reminder> reminders,
        IReadOnlyList<ApplicationDetailEmailRecord> emails,
        IReadOnlyList<ApplicationDetailActivityDto> recentActivity)
    {
        var requiresReview = emails.Any(email => email.RequiresManualReview);
        var highestConfidence = emails
            .Where(email => email.ClassificationConfidence.HasValue)
            .Select(email => email.ClassificationConfidence!.Value)
            .DefaultIfEmpty()
            .Max();
        var mostRecentEvent = recentActivity.FirstOrDefault();

        if (ApplicationWorkflowService.IsTerminalStatus(effectiveStatus))
        {
            return new ApplicationAgentBriefDto(
                effectiveStatus == ApplicationStatus.Rejected
                    ? "This application is rejected."
                    : "This application is closed.",
                "careeroc keeps the thread evidence for history, but this application should not create active follow-up work.",
                "No action is needed unless you intentionally reopen the application or inspect the historical evidence.",
                "Thread evidence",
                BuildConfidenceSummary(highestConfidence, hasBlockedReview: false));
        }

        if (requiresReview)
        {
            return new ApplicationAgentBriefDto(
                "The review agent is blocking trust.",
                "At least one linked thread still carries a review flag, so careeroc should not act on it until the evidence is resolved.",
                "Inspect the review agent decision and resolve the blocked thread before trusting any automated update.",
                "Review agent",
                BuildConfidenceSummary(highestConfidence, hasBlockedReview: true));
        }

        if (reminders.Count > 0)
        {
            var firstReminder = reminders[0];
            return new ApplicationAgentBriefDto(
                "A human follow-up is still open.",
                reminders.Count == 1
                    ? "1 reminder still blocks this workflow from moving forward cleanly."
                    : $"{reminders.Count} reminders still block this workflow from moving forward cleanly.",
                $"Work the next task: {firstReminder.Title}.",
                "Pending actions",
                BuildConfidenceSummary(highestConfidence, hasBlockedReview: false));
        }

        if (effectiveStatus == ApplicationStatus.Offer)
        {
            return new ApplicationAgentBriefDto(
                "An offer is live and deserves deliberate review.",
                "Offer-stage threads should be checked for deadlines, compensation details, and any follow-up evidence before the workflow is closed.",
                "Inspect the most recent offer thread and capture any response deadline or negotiation notes.",
                "Offer evidence",
                BuildConfidenceSummary(highestConfidence, hasBlockedReview: false));
        }

        if (effectiveStatus == ApplicationStatus.Interviewing)
        {
            return new ApplicationAgentBriefDto(
                "The interview loop is active.",
                "This application is already in interview motion, so the most useful surface is the latest thread evidence and any preparation reminders.",
                "Review the latest thread for scheduling changes, preparation asks, or missing follow-ups.",
                "Thread evidence",
                BuildConfidenceSummary(highestConfidence, hasBlockedReview: false));
        }

        return new ApplicationAgentBriefDto(
            "careeroc has structured the current recruiting state.",
            mostRecentEvent is null
                ? "The pipeline record exists, but there is not much event evidence yet."
                : $"The latest recorded change is: {mostRecentEvent.Description}.",
            "Inspect the latest thread evidence and the review agent decision before relying on the workflow state.",
            "Application detail",
            BuildConfidenceSummary(highestConfidence, hasBlockedReview: false));
    }

    private static ApplicationStatus ResolveEffectiveStatus(
        JobApplication application,
        IReadOnlyList<ApplicationDetailEmailRecord> emails)
    {
        if (!ApplicationWorkflowService.IsTerminalStatus(application.Status))
        {
            return application.Status;
        }

        var latestActiveEmail = emails
            .Where(
                email =>
                    email.Category.HasValue &&
                    ApplicationWorkflowService.IsActiveWorkflowCategory(email.Category.Value))
            .OrderByDescending(email => email.ReceivedAtUtc)
            .FirstOrDefault();

        if (latestActiveEmail is null)
        {
            return application.Status;
        }

        var latestTerminalEmailAtUtc = emails
            .Where(email => email.Category is EmailCategory.Rejection)
            .Select(email => (DateTimeOffset?)email.ReceivedAtUtc)
            .Max();

        if (latestTerminalEmailAtUtc.HasValue)
        {
            return latestActiveEmail.ReceivedAtUtc > latestTerminalEmailAtUtc.Value
                ? ApplicationWorkflowService.ToRecommendedStatus(latestActiveEmail.Category!.Value)
                : application.Status;
        }

        return latestActiveEmail.ReceivedAtUtc >= application.LastActivityAtUtc
            ? ApplicationWorkflowService.ToRecommendedStatus(latestActiveEmail.Category!.Value)
            : application.Status;
    }

    private static IReadOnlyList<string> BuildCompletedStages(
        JobApplication application,
        ApplicationStatus effectiveStatus,
        IReadOnlyList<ApplicationDetailEmailRecord> emails,
        IReadOnlyList<ApplicationDetailEventRecord> events)
    {
        var highestStageIndex = 0;

        ObserveStatus(application.Status);
        ObserveStatus(effectiveStatus);

        foreach (var email in emails)
        {
            if (email.Category.HasValue)
            {
                ObserveStatus(ApplicationWorkflowService.ToRecommendedStatus(email.Category.Value));
            }
        }

        foreach (var @event in events)
        {
            var eventStatus = ToPipelineStatus(@event.EventType);
            if (eventStatus.HasValue)
            {
                ObserveStatus(eventStatus.Value);
            }
        }

        return PipelineStatuses
            .Take(highestStageIndex + 1)
            .Select(ToLabel)
            .ToList();

        void ObserveStatus(ApplicationStatus status)
        {
            if (ApplicationWorkflowService.IsTerminalStatus(status))
            {
                return;
            }

            var index = Array.IndexOf(PipelineStatuses, status);
            if (index >= 0)
            {
                highestStageIndex = Math.Max(highestStageIndex, index);
            }
        }
    }

    private static ApplicationStatus? ToPipelineStatus(string eventType)
    {
        return eventType switch
        {
            "application_received" => ApplicationStatus.Applied,
            "recruiter_outreach" => ApplicationStatus.RecruiterContact,
            "oa_invitation" => ApplicationStatus.OaPending,
            "interview_invitation" or "interview_confirmation" => ApplicationStatus.Interviewing,
            "offer" => ApplicationStatus.Offer,
            _ => null
        };
    }

    private static string BuildConfidenceSummary(decimal highestConfidence, bool hasBlockedReview)
    {
        if (hasBlockedReview)
        {
            return "The review agent escalated at least one linked email because the extraction or match was not trustworthy enough.";
        }

        if (highestConfidence <= 0)
        {
            return "No model confidence metadata is attached yet; trust should come from the visible thread evidence.";
        }

        return $"Linked evidence reached up to {highestConfidence:0.00} classification confidence. Use thread evidence and review agent decisions to confirm the workflow state.";
    }

    private static ApplicationDetailEmailRecord? ResolveReminderEvidence(
        Reminder reminder,
        IReadOnlyList<ApplicationDetailEmailRecord> emails)
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

    private static long ScoreReminderEvidence(Reminder reminder, ApplicationDetailEmailRecord email)
    {
        if (!email.DueDateUtc.HasValue)
        {
            return long.MaxValue / 4;
        }

        return Math.Abs((email.DueDateUtc.Value - reminder.DueAtUtc).Ticks);
    }

    private static List<ApplicationThreadDto> BuildThreads(
        IReadOnlyList<ApplicationDetailEmailRecord> emails,
        bool suppressActions)
    {
        return emails
            .GroupBy(email => string.IsNullOrWhiteSpace(email.ThreadId) ? $"email:{email.Id}" : email.ThreadId, StringComparer.Ordinal)
            .Select(
                group =>
                {
                    var orderedMessages = group
                        .OrderByDescending(email => email.ReceivedAtUtc)
                        .ToList();
                    var latest = orderedMessages[0];

                    return new ApplicationThreadDto(
                        group.Key,
                        latest.Subject,
                        latest.ReceivedAtUtc,
                        group.Count(),
                        !suppressActions && group.Any(email => email.ActionRequired),
                        group.Any(email => email.RequiresManualReview),
                        group.Any(email => email.IsOutbound),
                        latest.Category.HasValue ? ToSnakeCase(latest.Category.Value) : null,
                        BuildMessageSummary(latest),
                        orderedMessages
                            .Take(6)
                            .OrderBy(email => email.ReceivedAtUtc)
                            .Select(
                                email => new ApplicationThreadMessageDto(
                                    email.Id,
                                    email.Sender,
                                    email.Subject,
                                    BuildMessageSummary(email),
                                    ApplicationDetailEmailTextBuilder.BuildSnippet(email.ParsedBody, email.RawBody),
                                    ApplicationDetailEmailTextBuilder.BuildBodyPreview(email.ParsedBody, email.RawBody),
                                    email.ReceivedAtUtc,
                                    email.IsOutbound,
                                    email.Category.HasValue ? ToSnakeCase(email.Category.Value) : null,
                                    !suppressActions && email.ActionRequired,
                                    suppressActions ? null : email.DueDateUtc,
                                    email.ClassificationConfidence,
                                    email.MatchingConfidence,
                                    email.ContactName,
                                    email.ContactEmail,
                                    email.ContactType.HasValue ? ToLabel(email.ContactType.Value) : null,
                                    email.ContactRoleHint,
                                    email.RequiresManualReview,
                                    email.ReviewReason,
                                    ExtractProcessingSource(email),
                                    !string.IsNullOrWhiteSpace(email.ProcessingNotes),
                                    BuildGmailUrl(email),
                                    AgentTraceSummaryBuilder.FromInboxEmailTrace(
                                        email.Id,
                                        email.ThreadId,
                                        email.ClassificationConfidence,
                                        email.RequiresManualReview,
                                        email.ProcessingTraceJson),
                                    BuildAgentTrace(email)))
                            .ToList(),
                        BuildGmailUrl(latest));
                })
            .OrderByDescending(thread => thread.LastMessageAtUtc)
            .ToList();
    }

    private static List<ApplicationArtifactEvidenceDto> BuildArtifactEvidence(
        IReadOnlyList<ApplicationArtifactEvidence> artifacts)
    {
        return artifacts
            .Select(
                evidence => new ApplicationArtifactEvidenceDto(
                    evidence.Id,
                    evidence.JobApplicationId,
                    evidence.ArtifactKind,
                    evidence.FileName,
                    evidence.ContentType,
                    evidence.SafeTitle,
                    evidence.ExtractedFactType,
                    evidence.ExtractedFactText,
                    evidence.NormalizedDateUtc,
                    evidence.Confidence,
                    evidence.SourceSnippet,
                    evidence.RequiresReview,
                    evidence.ReviewReason,
                    evidence.CreatedAtUtc,
                    AgentTraceSummaryBuilder.FromArtifactEvidence(evidence),
                    BuildArtifactAgentTrace(evidence)))
            .ToList();
    }

    private static ReviewAgentTraceDto BuildArtifactAgentTrace(ApplicationArtifactEvidence evidence)
    {
        var summary = AgentTraceSummaryBuilder.FromArtifactEvidence(evidence);

        return new ReviewAgentTraceDto(
            evidence.CreatedAtUtc,
            [
                new ReviewAgentTraceStepDto(
                    "artifact_ingestion",
                    "Artifact evidence importer",
                    $"Accepted {evidence.ContentType} evidence as {ToLabel(evidence.ArtifactKind)}.",
                    "The artifact path stores evidence metadata and does not keep raw uploaded or downloaded bytes.",
                    [
                        new ReviewAgentTraceFactDto("file", evidence.FileName),
                        new ReviewAgentTraceFactDto("source", summary.EvidenceSource)
                    ]),
                new ReviewAgentTraceStepDto(
                    "gemma_extraction",
                    "Artifact extractor",
                    evidence.ExtractedFactText,
                    evidence.SourceSnippet,
                    [
                        new ReviewAgentTraceFactDto("model", summary.ModelPath ?? "not recorded"),
                        new ReviewAgentTraceFactDto("purpose", summary.Purpose),
                        new ReviewAgentTraceFactDto("confidence", $"{Math.Round(evidence.Confidence * 100)}%")
                    ]),
                new ReviewAgentTraceStepDto(
                    "review_gate",
                    "Artifact review gate",
                    evidence.RequiresReview
                        ? "Blocked this artifact from mutating application state."
                        : "Recorded evidence without mutating application state.",
                    evidence.ReviewReason ??
                    "Artifact evidence remains an auditable card; application status and reminders require a separate review path.",
                    [
                        new ReviewAgentTraceFactDto("outcome", summary.ReviewGateResult),
                        new ReviewAgentTraceFactDto("fallback", summary.FallbackPath)
                    ])
            ],
            summary);
    }

    private static ReviewAgentTraceDto BuildApplicationAgentTrace(
        JobApplication application,
        ApplicationStatus effectiveStatus,
        IReadOnlyList<Reminder> reminders,
        IReadOnlyList<ApplicationDetailEmailRecord> emails,
        IReadOnlyList<ApplicationDetailActivityDto> recentActivity)
    {
        var flaggedMessages = emails.Count(email => email.RequiresManualReview);
        var latestActivity = recentActivity.FirstOrDefault()?.Description ?? "No recent activity";
        var agentBrief = BuildAgentBrief(application, effectiveStatus, reminders, emails, recentActivity);
        var summary = new AgentTraceSummaryDto(
            null,
            "application_detail_orchestrator",
            null,
            "application_detail",
            flaggedMessages > 0 ? "review_gated_messages_present" : "no_blocking_messages",
            "deterministic_application_detail_orchestrator");

        return new ReviewAgentTraceDto(
            application.LastActivityAtUtc,
            [
                new ReviewAgentTraceStepDto(
                    "strategy_snapshot",
                    "Application orchestrator",
                    agentBrief.Summary,
                    agentBrief.ConfidenceSummary,
                    [
                        new ReviewAgentTraceFactDto("status", ToLabel(effectiveStatus)),
                        new ReviewAgentTraceFactDto("recommended surface", agentBrief.RecommendedSurface),
                        new ReviewAgentTraceFactDto("latest activity", latestActivity)
                    ]),
                new ReviewAgentTraceStepDto(
                    "evidence_links",
                    "Application evidence linker",
                    "Connected threads, artifacts, reminders, contacts, and activity into the application brief.",
                    "The orchestrator brief is grounded in application detail evidence already visible on this page.",
                    [
                        new ReviewAgentTraceFactDto("messages", emails.Count.ToString(CultureInfo.InvariantCulture)),
                        new ReviewAgentTraceFactDto("open actions", reminders.Count.ToString(CultureInfo.InvariantCulture)),
                        new ReviewAgentTraceFactDto("recent activity", recentActivity.Count.ToString(CultureInfo.InvariantCulture))
                    ]),
                new ReviewAgentTraceStepDto(
                    "review_gate",
                    "Application trust gate",
                    flaggedMessages > 0
                        ? "Review-gated messages keep this strategy from being fully trusted."
                        : "No linked message is currently blocking the application strategy.",
                    agentBrief.NextAction,
                    [
                        new ReviewAgentTraceFactDto("flagged messages", flaggedMessages.ToString(CultureInfo.InvariantCulture)),
                        new ReviewAgentTraceFactDto("runtime", "deterministic.application_detail_orchestrator.v1")
                    ])
            ],
            summary);
    }

    private static List<ApplicationContactDto> BuildContacts(IReadOnlyList<ApplicationDetailEmailRecord> emails)
    {
        return emails
            .Where(
                email =>
                    !string.IsNullOrWhiteSpace(email.ContactEmail) ||
                    !string.IsNullOrWhiteSpace(email.ContactName))
            .GroupBy(
                email => BuildContactKey(email),
                StringComparer.OrdinalIgnoreCase)
            .Select(
                group =>
                {
                    var latest = group
                        .OrderByDescending(email => email.ReceivedAtUtc)
                        .First();

                    return new ApplicationContactDto(
                        latest.ContactName,
                        latest.ContactEmail,
                        latest.ContactType.HasValue ? ToLabel(latest.ContactType.Value) : null,
                        latest.ContactRoleHint,
                        latest.ReceivedAtUtc,
                        latest.Subject,
                        group.Count());
                })
            .OrderByDescending(contact => contact.LastSeenAtUtc)
            .ToList();
    }

    private static List<ApplicationFeedbackNoteDto> BuildFeedbackNotes(IReadOnlyList<ApplicationDetailEmailRecord> emails)
    {
        return emails
            .Where(email => !string.IsNullOrWhiteSpace(email.ProcessingNotes))
            .OrderByDescending(email => email.ReviewedAtUtc ?? email.ProcessedAtUtc ?? email.ReceivedAtUtc)
            .Take(8)
            .Select(
                email => new ApplicationFeedbackNoteDto(
                    email.Id,
                    email.Subject,
                    email.ProcessingNotes!.Trim(),
                    ExtractProcessingSource(email) ?? "Operator note",
                    email.ReviewedAtUtc ?? email.ProcessedAtUtc ?? email.ReceivedAtUtc))
            .ToList();
    }

    private static string BuildMessageSummary(ApplicationDetailEmailRecord email)
    {
        return ApplicationDetailEmailTextBuilder.BuildMessageSummary(
            email.ProcessingTraceJson,
            email.RequiresManualReview,
            email.ReviewReason,
            email.IsOutbound,
            email.Category,
            email.ParsedBody,
            email.RawBody);
    }

    private static string? ExtractProcessingSource(ApplicationDetailEmailRecord email)
    {
        var trace = InboxEmailAgentTraceJson.Deserialize(email.ProcessingTraceJson);
        var traceSource = InboxEmailAgentTraceJson.ExtractProcessingSource(trace);
        if (!string.IsNullOrWhiteSpace(traceSource))
        {
            return traceSource;
        }

        return ExtractLegacyProcessingSource(email.ProcessingNotes);
    }

    private static ReviewAgentTraceDto? BuildAgentTrace(ApplicationDetailEmailRecord email)
    {
        var trace = InboxEmailAgentTraceJson.Deserialize(email.ProcessingTraceJson);
        if (trace is null)
        {
            return null;
        }

        return new ReviewAgentTraceDto(
            trace.GeneratedAtUtc,
            trace.Steps
                .Select(
                    step => new ReviewAgentTraceStepDto(
                        step.Stage,
                        step.AgentName,
                        step.Summary,
                        step.Reason,
                        step.Facts
                            .Select(fact => new ReviewAgentTraceFactDto(fact.Label, fact.Value))
                            .ToArray()))
                .ToArray(),
            AgentTraceSummaryBuilder.FromInboxEmailTrace(
                email.Id,
                email.ThreadId,
                email.ClassificationConfidence,
                email.RequiresManualReview,
                email.ProcessingTraceJson));
    }

    private static AgentTraceStep? FindTraceStep(InboxEmailAgentTrace? trace, string stage)
    {
        return trace?.Steps.FirstOrDefault(
            step => string.Equals(step.Stage, stage, StringComparison.Ordinal));
    }

    private static string? ExtractLegacyProcessingSource(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes) || !notes.StartsWith("[", StringComparison.Ordinal))
        {
            return null;
        }

        var closingBracketIndex = notes.IndexOf(']');
        if (closingBracketIndex <= 1)
        {
            return null;
        }

        return notes[1..closingBracketIndex].Trim();
    }

    private static string BuildContactKey(ApplicationDetailEmailRecord email)
    {
        return !string.IsNullOrWhiteSpace(email.ContactEmail)
            ? email.ContactEmail.Trim().ToLowerInvariant()
            : email.ContactName!.Trim().ToLowerInvariant();
    }

    private static string? BuildGmailUrl(ApplicationDetailEmailRecord email)
    {
        return InboxEmailLinkBuilder.BuildGmailUrl(
            email.Provider,
            email.AccountEmailAddress,
            email.ThreadId,
            email.ExternalMessageId);
    }

    private sealed record ApplicationDetailEmailRecord(
        Guid Id,
        string Provider,
        string AccountEmailAddress,
        string ExternalMessageId,
        string ThreadId,
        string Sender,
        string Subject,
        string ParsedBody,
        string RawBody,
        bool IsOutbound,
        EmailCategory? Category,
        string? ContactName,
        string? ContactEmail,
        string? ContactRoleHint,
        RecruitingContactType? ContactType,
        bool ActionRequired,
        DateTimeOffset? DueDateUtc,
        decimal? ClassificationConfidence,
        decimal? MatchingConfidence,
        bool RequiresManualReview,
        string? ReviewReason,
        DateTimeOffset? ReviewedAtUtc,
        string? ProcessingNotes,
        string? ProcessingTraceJson,
        DateTimeOffset? ProcessedAtUtc,
        DateTimeOffset ReceivedAtUtc);

    private sealed record ApplicationDetailEventRecord(
        Guid Id,
        string EventType,
        DateTimeOffset EventTimeUtc);

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
            "gmail_reply_detected" => $"Sent Gmail reply found for {company}",
            "manual_stage_update" => $"Application status updated manually for {company}",
            _ => $"Inbox activity updated for {company}"
        };
    }

    private static string ToSnakeCase<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return string.Concat(
            value.ToString().Select(
                (character, index) =>
                    index > 0 && char.IsUpper(character)
                        ? $"_{char.ToLowerInvariant(character)}"
                        : char.ToLowerInvariant(character).ToString()));
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

    private static string ToLabel(string value)
    {
        return string.Join(
            " ",
            value
                .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static string ResolveUserEmail(string? userEmail)
    {
        return ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
    }

}
