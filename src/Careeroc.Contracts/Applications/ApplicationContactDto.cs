namespace Careeroc.Contracts.Applications;

public sealed record ApplicationContactDto(
    string? Name,
    string? Email,
    string? Type,
    string? RoleHint,
    DateTimeOffset LastSeenAtUtc,
    string LatestSubject,
    int MessageCount);
