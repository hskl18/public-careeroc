namespace Careeroc.Contracts.Review;

using Careeroc.Contracts.Agents;

public sealed record ReviewEmailDto(
    Guid Id,
    string ThreadId,
    string Sender,
    string Subject,
    string Snippet,
    string BodyPreview,
    DateTimeOffset ReceivedAtUtc,
    decimal? ClassificationConfidence,
    decimal? MatchingConfidence,
    bool RequiresManualReview,
    string? ReviewReason,
    string? Category,
    string? ExtractedCompany,
    string? ExtractedRole,
    string? ContactName,
    string? ContactEmail,
    string? ContactType,
    string? ContactRoleHint,
    bool ActionRequired,
    DateTimeOffset? DueDateUtc,
    string? Notes,
    string? ProcessingSource,
    ReviewAgentTraceDto? AgentTrace,
    Guid? JobApplicationId,
    IReadOnlyList<ReviewThreadMessageDto> ThreadMessages,
    AgentTraceSummaryDto? TraceSummary = null);
