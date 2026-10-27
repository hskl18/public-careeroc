namespace Careeroc.Application.Models;

public sealed record ResumeVisionTextExtractionRequest(
    string FileName,
    string ContentType,
    string Base64Image,
    string SourceKind);
