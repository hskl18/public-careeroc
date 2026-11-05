namespace Careeroc.Application.Models;

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
