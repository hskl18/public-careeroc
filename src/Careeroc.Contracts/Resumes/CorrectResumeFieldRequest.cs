namespace Careeroc.Contracts.Resumes;

public sealed record CorrectResumeFieldRequest(
    string FieldPath,
    string? PreviousValue,
    string CorrectedValue,
    string? Note);
