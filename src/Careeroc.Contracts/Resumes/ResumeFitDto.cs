namespace Careeroc.Contracts.Resumes;

public sealed record ResumeFitDto(
    Guid JobApplicationId,
    Guid? ResumeVersionId,
    int MatchScore,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Gaps,
    string Rationale);

public sealed record ResumeSuggestionDto(
    Guid Id,
    string Kind,
    string TargetSection,
    string OriginalText,
    string SuggestedText,
    string Rationale,
    IReadOnlyList<string> EvidenceLinks);
