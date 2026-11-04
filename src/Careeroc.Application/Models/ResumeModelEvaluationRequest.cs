namespace Careeroc.Application.Models;

public sealed record ResumeModelEvaluationRequest(
    string SourceText,
    string Summary,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> ProjectHighlights,
    int ExperienceCount,
    int ProjectCount,
    bool HasEducation,
    bool HasContact,
    bool HasLinks,
    string? TargetRole,
    int ApplicationCount,
    int InterviewOrOfferCount,
    IReadOnlyList<ResumeModelCorrection> Corrections,
    ResumeMailboxContext? MailboxContext = null);

public sealed record ResumeModelCorrection(
    string FieldPath,
    string CorrectedValue,
    string? Note);

public sealed record ResumeMailboxContext(
    IReadOnlyList<string> TopTargetRoles,
    IReadOnlyList<string> CompaniesWithPositiveOutcomes,
    IReadOnlyList<string> RepeatedRejectedRoleFamilies,
    IReadOnlyList<string> LatestActionSignals,
    Guid? ResumeVersionLikelyUsedBeforePositiveOutcomes,
    IReadOnlyList<string> ConfirmedMemoryFacts,
    IReadOnlyList<ResumeMailboxEvidenceLink>? EvidenceLinks = null);

public sealed record ResumeMailboxEvidenceLink(
    string SourceType,
    Guid SourceId,
    string Label,
    DateTimeOffset ObservedAtUtc);
