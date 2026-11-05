namespace Careeroc.Contracts.Resumes;

public sealed record ResumeFitDto(
    Guid JobApplicationId,
    Guid? ResumeVersionId,
    int MatchScore,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Gaps,
    string Rationale);
