namespace Careeroc.Application.Models;

public sealed record CareerAgentGraphReviewRequest(
    string ThreadId,
    DateTimeOffset RequestedAtUtc,
    string BaselineSummary,
    IReadOnlyList<string> BaselineFindings,
    IReadOnlyList<string> BaselineNextActions,
    CareerAgentGraphEvidence Evidence,
    CareerAgentGraphResumeEvidence? LatestResume,
    IReadOnlyList<CareerAgentGraphApplicationEvidence> Applications,
    IReadOnlyList<string> TopTargetRoles,
    IReadOnlyList<string> PositiveOutcomes,
    IReadOnlyList<string> RepeatedRejections,
    IReadOnlyList<string> LatestActionSignals,
    IReadOnlyList<string> ConfirmedMemoryFacts);

public sealed record CareerAgentGraphEvidence(
    int ResumeVersions,
    int LatestResumeScore,
    int TotalApplications,
    int InterviewOrOfferApplications,
    int RejectedApplications,
    Guid? ResumeVersionLinkedToOutcome);

public sealed record CareerAgentGraphResumeEvidence(
    string Title,
    string Summary,
    int Score,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> ProjectHighlights);

public sealed record CareerAgentGraphApplicationEvidence(
    Guid Id,
    string Company,
    string Role,
    string Status,
    DateTimeOffset LastActivityAtUtc,
    bool HasOpenReminder);

public sealed record CareerAgentGraphReviewResponse(
    string ThreadId,
    string Status,
    string RuntimePath,
    string ModelPath,
    string Summary,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> NextActions,
    bool RequiresHumanReview,
    decimal Confidence,
    IReadOnlyList<CareerAgentGraphTraceStep> Trace,
    IReadOnlyList<CareerAgentGraphReminderProposal>? ProposedReminders = null);

// The runtime names applications by the ids it was given; the API re-checks ownership before writing.
public sealed record CareerAgentGraphReminderProposal(string ApplicationId, string Title, int DueInDays);

public sealed record CareerAgentGraphTraceStep(
    string Stage,
    string AgentName,
    string Summary,
    string? Reason,
    IReadOnlyList<CareerAgentGraphTraceFact> Facts);

public sealed record CareerAgentGraphTraceFact(string Label, string Value);

public sealed record CareerAgentGraphRunOutcome(
    bool Attempted,
    CareerAgentGraphReviewResponse? Response,
    string? FailureReason)
{
    public static CareerAgentGraphRunOutcome Skipped()
    {
        return new CareerAgentGraphRunOutcome(false, null, null);
    }

    public static CareerAgentGraphRunOutcome Succeeded(CareerAgentGraphReviewResponse response)
    {
        return new CareerAgentGraphRunOutcome(true, response, null);
    }

    public static CareerAgentGraphRunOutcome Failed(string reason)
    {
        return new CareerAgentGraphRunOutcome(true, null, reason);
    }
}
