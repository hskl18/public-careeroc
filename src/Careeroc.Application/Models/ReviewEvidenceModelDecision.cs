namespace Careeroc.Application.Models;

public sealed record ReviewEvidenceModelDecision(
    bool RequiresManualReview,
    string? Reason,
    string VisibleOutput,
    string AgentName,
    decimal Confidence);

