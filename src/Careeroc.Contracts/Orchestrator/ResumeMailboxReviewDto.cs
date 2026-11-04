using Careeroc.Contracts.Review;

namespace Careeroc.Contracts.Orchestrator;

public sealed record ResumeMailboxReviewDto(
    DateTimeOffset GeneratedAtUtc,
    string RuntimePath,
    string ModelPath,
    string Summary,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> NextActions,
    ResumeMailboxReviewEvidenceDto Evidence,
    string? ThreadId = null,
    string Status = "completed",
    bool RequiresHumanReview = false,
    decimal? Confidence = null,
    ReviewAgentTraceDto? Trace = null,
    IReadOnlyList<ProposedReminderDto>? ProposedReminders = null,
    int? RemindersCreated = null);

public sealed record ProposedReminderDto(
    Guid ApplicationId,
    string Company,
    string Role,
    string Title,
    int DueInDays);

public sealed record ResumeMailboxReviewEvidenceDto(
    int ResumeVersions,
    int LatestResumeScore,
    int TotalApplications,
    int InterviewOrOfferApplications,
    int RejectedApplications,
    Guid? ResumeVersionLinkedToOutcome);
