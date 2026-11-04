namespace Careeroc.Contracts.Admin;

public sealed record AdminSummaryResponse(
    AdminQueueSummaryDto Queue,
    IReadOnlyList<AdminModelCallEventDto> RecentModelCallEvents,
    IReadOnlyList<AdminOperationalCheckDto> OperationalChecks,
    IReadOnlyList<AdminFailedEmailJobDto> RecentFailedEmailJobs);

public sealed record AdminQueueSummaryDto(
    int PendingAnalysisJobs,
    int ProcessingAnalysisJobs,
    int FailedAnalysisJobs);

public sealed record AdminModelCallEventDto(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string Provider,
    string Model,
    string Purpose,
    string Outcome,
    string? UserEmail,
    Guid? InboxAccountId,
    Guid? InboxEmailId,
    string? RunId,
    string? CorrelationId,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    decimal? EstimatedCostUsd,
    int LatencyMs,
    int? StatusCode,
    string? ErrorType);

public sealed record AdminOperationalCheckDto(
    string Area,
    string Name,
    string Status,
    string Detail,
    string? CurrentValue);

public sealed record AdminFailedEmailJobDto(
    Guid JobId,
    Guid InboxEmailId,
    Guid InboxAccountId,
    string UserEmail,
    string InboxEmailAddress,
    int Attempts,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LockedAtUtc,
    string? LastError);
