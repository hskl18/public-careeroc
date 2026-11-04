namespace Careeroc.Contracts.Dashboard;

public sealed record PendingActionDto(
    Guid Id,
    Guid JobApplicationId,
    string Company,
    string Role,
    string Title,
    DateTimeOffset DueAtUtc,
    string Source,
    string Status,
    Guid? SourceEmailId,
    string? SourceThreadId,
    string? SourceMessageId,
    bool HasExplicitDueDate,
    string? GmailUrl,
    string? TaskType = null,
    string? SourceSubject = null,
    string? SourceSender = null,
    DateTimeOffset? ReceivedAtUtc = null,
    bool DeadlineFound = false,
    string? DeadlineHint = null,
    string? Reason = null);
