namespace Careeroc.Contracts.Review;

public sealed record ReviewApplicationDto(
    Guid Id,
    string Company,
    string Role,
    string Status,
    int Priority,
    DateTimeOffset LastActivityAtUtc);
