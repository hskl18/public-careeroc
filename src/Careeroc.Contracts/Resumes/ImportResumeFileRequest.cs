namespace Careeroc.Contracts.Resumes;

public sealed record ImportResumeFileRequest(
    string? Title,
    string FileName,
    string ContentType,
    string Base64Content);
