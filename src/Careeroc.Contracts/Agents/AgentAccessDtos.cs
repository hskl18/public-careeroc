namespace Careeroc.Contracts.Agents;

public sealed record CreatePersonalAccessTokenRequest(string Name, int? ExpiresInDays = null);

public sealed record PersonalAccessTokenDto(
    Guid Id,
    string Name,
    string TokenPrefix,
    string Scope,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc);

// The plaintext token appears only in this response; the server keeps a hash.
public sealed record CreatedPersonalAccessTokenDto(string Token, PersonalAccessTokenDto Details);

public sealed record AgentApplicationDto(
    Guid Id,
    string Company,
    string Role,
    string Status,
    DateTimeOffset LastActivityAtUtc);

public sealed record AgentReminderDto(
    string Company,
    string Role,
    string Title,
    DateTimeOffset DueAtUtc,
    bool IsOverdue);

public sealed record AgentPipelineSummaryDto(
    int TotalApplications,
    IReadOnlyDictionary<string, int> ApplicationsByStatus,
    IReadOnlyList<AgentReminderDto> OpenReminders,
    IReadOnlyList<AgentApplicationDto> RecentlyActive,
    DateTimeOffset GeneratedAtUtc);

public sealed record AgentTimelineEventDto(string EventType, DateTimeOffset EventTimeUtc, string? Subject);

public sealed record AgentApplicationDetailDto(
    AgentApplicationDto Application,
    IReadOnlyList<AgentTimelineEventDto> Timeline,
    IReadOnlyList<AgentReminderDto> OpenReminders);

public sealed record AgentCallerDto(string Owner);
