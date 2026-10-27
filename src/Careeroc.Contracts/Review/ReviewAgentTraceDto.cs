namespace Careeroc.Contracts.Review;

using Careeroc.Contracts.Agents;

public sealed record ReviewAgentTraceDto(
    DateTimeOffset? GeneratedAtUtc,
    IReadOnlyList<ReviewAgentTraceStepDto> Steps,
    AgentTraceSummaryDto? Summary = null);

public sealed record ReviewAgentTraceStepDto(
    string Stage,
    string AgentName,
    string Summary,
    string? Reason,
    IReadOnlyList<ReviewAgentTraceFactDto> Facts);

public sealed record ReviewAgentTraceFactDto(
    string Label,
    string Value);
