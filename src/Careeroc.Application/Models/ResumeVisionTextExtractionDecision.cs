namespace Careeroc.Application.Models;

public sealed record ResumeVisionTextExtractionDecision(
    string ExtractedText,
    decimal Confidence,
    string AgentName);
