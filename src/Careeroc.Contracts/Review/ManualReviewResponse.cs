namespace Careeroc.Contracts.Review;

using Careeroc.Contracts.Agents;

public sealed record ManualReviewResponse(
    IReadOnlyList<ReviewEmailDto> Emails,
    IReadOnlyList<ReviewArtifactEvidenceDto> Artifacts,
    IReadOnlyList<ReviewApplicationDto> Applications,
    IReadOnlyList<string> CategoryOptions,
    IReadOnlyList<string> StatusOptions,
    int TotalWaiting,
    int TotalUnassigned,
    int TotalArtifactReviews);

public sealed record ReviewArtifactEvidenceDto(
    Guid Id,
    Guid JobApplicationId,
    string Company,
    string Role,
    string ArtifactKind,
    string FileName,
    string SafeTitle,
    string ExtractedFactType,
    string ExtractedFactText,
    DateTimeOffset? NormalizedDateUtc,
    decimal Confidence,
    string SourceSnippet,
    string ReviewReason,
    DateTimeOffset CreatedAtUtc,
    AgentTraceSummaryDto TraceSummary,
    ReviewAgentTraceDto? AgentTrace = null);
