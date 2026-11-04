namespace Careeroc.Contracts.Profile;

public sealed record UserProfileEvaluationDto(
    int ReadinessScore,
    string Summary,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> NextActions,
    UserProfileEvaluationEvidenceDto Evidence);

public sealed record UserProfileEvaluationEvidenceDto(
    int TotalApplications,
    int ActiveApplications,
    int InterviewingApplications,
    int Offers,
    int ConnectedInboxes,
    int TotalInboxes,
    int MemoryEntries,
    DateTimeOffset? LastApplicationActivityAtUtc,
    int ResumeVersions,
    int? LatestResumeScore,
    string? LatestResumeTitle,
    string? ResumeMailboxSummary);
