namespace Careeroc.Application.Models;

public sealed record ModelCallEventRecord(
    DateTimeOffset OccurredAtUtc,
    string Provider,
    string Model,
    string Purpose,
    string Outcome,
    int LatencyMs,
    string? UserEmail = null,
    Guid? InboxAccountId = null,
    Guid? InboxEmailId = null,
    string? RunId = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    decimal? EstimatedCostUsd = null,
    int? StatusCode = null,
    string? ErrorType = null,
    string? CorrelationId = null);
