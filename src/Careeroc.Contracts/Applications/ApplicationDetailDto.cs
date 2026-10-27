namespace Careeroc.Contracts.Applications;

using Careeroc.Contracts.Agents;
using Careeroc.Contracts.Profile;
using Careeroc.Contracts.Review;

public sealed record ApplicationDetailDto(
    Guid Id,
    string Company,
    string Role,
    string Status,
    IReadOnlyList<string> CompletedStages,
    int Priority,
    string Source,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    bool ActionRequired,
    ApplicationAgentBriefDto AgentBrief,
    IReadOnlyList<ApplicationContactDto> Contacts,
    IReadOnlyList<ApplicationDetailReminderDto> PendingActions,
    IReadOnlyList<ApplicationDetailActivityDto> RecentActivity,
    IReadOnlyList<ApplicationThreadDto> Threads,
    IReadOnlyList<ApplicationFeedbackNoteDto> FeedbackNotes,
    IReadOnlyList<ApplicationArtifactEvidenceDto>? ArtifactEvidence = null,
    int RecentActivityTotalCount = 0,
    int ArtifactEvidenceTotalCount = 0,
    IReadOnlyList<UserMemoryEntryDto>? MemoryProposals = null,
    ReviewAgentTraceDto? AgentTrace = null);

public sealed record ApplicationAgentBriefDto(
    string Headline,
    string Summary,
    string NextAction,
    string RecommendedSurface,
    string ConfidenceSummary);

public sealed record ApplicationDetailReminderDto(
    Guid Id,
    string Title,
    DateTimeOffset DueAtUtc,
    string Source,
    string Status,
    Guid? SourceEmailId,
    string? SourceThreadId,
    string? SourceMessageId,
    bool HasExplicitDueDate,
    string? GmailUrl);

public sealed record ApplicationDetailActivityDto(
    Guid Id,
    string Description,
    string Category,
    DateTimeOffset OccurredAtUtc);

public sealed record ApplicationThreadDto(
    string ThreadId,
    string Title,
    DateTimeOffset LastMessageAtUtc,
    int MessageCount,
    bool ActionRequired,
    bool RequiresManualReview,
    bool HasOutboundReply,
    string? StageLabel,
    string Snippet,
    IReadOnlyList<ApplicationThreadMessageDto> Messages,
    string? GmailUrl);

public sealed record ApplicationThreadMessageDto(
    Guid Id,
    string Sender,
    string Subject,
    string Summary,
    string Snippet,
    string BodyPreview,
    DateTimeOffset ReceivedAtUtc,
    bool IsOutbound,
    string? Category,
    bool ActionRequired,
    DateTimeOffset? DueDateUtc,
    decimal? ClassificationConfidence,
    decimal? MatchingConfidence,
    string? ContactName,
    string? ContactEmail,
    string? ContactType,
    string? ContactRoleHint,
    bool RequiresManualReview,
    string? ReviewReason,
    string? ProcessingSource,
    bool HasFeedbackNote,
    string? GmailUrl,
    AgentTraceSummaryDto? TraceSummary = null,
    ReviewAgentTraceDto? AgentTrace = null);

public sealed record ApplicationFeedbackNoteDto(
    Guid EmailId,
    string Subject,
    string Note,
    string Source,
    DateTimeOffset OccurredAtUtc);

public sealed record ApplicationArtifactInputDto(
    string ArtifactKind,
    string FileName,
    string ContentType,
    string Base64Content,
    string? SourceLabel = null);

public sealed record ReviewApplicationArtifactRequest(
    string Outcome,
    string? Notes = null);

public sealed record ApplicationArtifactEvidenceDto(
    Guid Id,
    Guid JobApplicationId,
    string ArtifactKind,
    string FileName,
    string ContentType,
    string SafeTitle,
    string ExtractedFactType,
    string ExtractedFactText,
    DateTimeOffset? NormalizedDateUtc,
    decimal Confidence,
    string SourceSnippet,
    bool RequiresReview,
    string? ReviewReason,
    DateTimeOffset CreatedAtUtc,
    AgentTraceSummaryDto TraceSummary,
    ReviewAgentTraceDto? AgentTrace = null);
