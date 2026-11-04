namespace Careeroc.Contracts.Resumes;

public sealed record CreateResumeRequest(
    string? Title,
    string? SourceKind,
    string SourceText,
    string? SourceFileName = null);
