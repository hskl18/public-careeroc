namespace Careeroc.Contracts.Applications;

public sealed record ApplicationSummaryNextActionDto(
    string Title,
    DateTimeOffset? DueAtUtc,
    bool HasExplicitDueDate,
    string Tone,
    Guid? SourceReminderId);

public sealed record ApplicationSummaryEvidenceDto(
    Guid EmailId,
    string Summary,
    string? Subject,
    string? Sender,
    string? Snippet,
    DateTimeOffset OccurredAtUtc,
    string Category,
    string? GmailUrl);

public sealed record ApplicationSummaryDto(
    Guid Id,
    string Company,
    string Role,
    string Status,
    int Priority,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    string UpdatedFrom,
    bool ActionRequired,
    string? GmailUrl,
    bool HasOutboundReply,
    DateTimeOffset? LastOutboundReplyAtUtc,
    ApplicationSummaryNextActionDto? NextAction = null,
    string? PriorityLabel = null,
    string? PriorityReason = null,
    decimal? ParserConfidence = null,
    bool RequiresReview = false,
    string? ReviewReason = null,
    ApplicationSummaryEvidenceDto? LatestEvidence = null,
    int UrgencyScore = 0,
    DateTimeOffset? NextActionDueAtUtc = null);
