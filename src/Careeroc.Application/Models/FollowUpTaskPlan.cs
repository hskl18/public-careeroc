using Careeroc.Domain.Enums;

namespace Careeroc.Application.Models;

public sealed record FollowUpTaskPlan(
    bool ShouldCreate,
    ReminderType Type,
    string Title,
    DateTimeOffset DueAtUtc,
    string Reason,
    string AgentName,
    string? ToolName = null,
    string? ToolArgumentsJson = null);

public sealed record FollowUpTaskToolCall(
    string Name,
    FollowUpTaskToolArguments Arguments,
    string Reason);

public sealed record FollowUpTaskToolArguments(
    bool ShouldCreate,
    ReminderType Type,
    string Title,
    DateTimeOffset DueAtUtc);
