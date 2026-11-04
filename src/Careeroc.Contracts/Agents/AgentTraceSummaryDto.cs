namespace Careeroc.Contracts.Agents;

public sealed record AgentTraceSummaryDto(
    string? ModelPath,
    string Purpose,
    decimal? Confidence,
    string EvidenceSource,
    string ReviewGateResult,
    string FallbackPath);
