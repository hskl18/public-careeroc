namespace Careeroc.Contracts.Applications;

public sealed record ApplicationLookupResponse(
    IReadOnlyList<ApplicationLookupItemDto> Items);

public sealed record ApplicationLookupItemDto(
    Guid Id,
    string Company,
    string Role,
    string Status,
    DateTimeOffset LastActivityAtUtc,
    string? Source,
    string? Location,
    int UrgencyScore = 0,
    DateTimeOffset? NextActionDueAtUtc = null);
