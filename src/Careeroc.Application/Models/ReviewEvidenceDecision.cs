namespace Careeroc.Application.Models;

public sealed record ReviewEvidenceDecision(
    bool RequiresManualReview,
    string? Reason,
    string VisibleOutput,
    string AgentName);
