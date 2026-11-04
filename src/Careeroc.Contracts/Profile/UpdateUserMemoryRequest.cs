namespace Careeroc.Contracts.Profile;

public sealed record UpdateUserMemoryRequest(
    string Value,
    string? Summary,
    double? Confidence,
    DateTimeOffset? ExpiresAtUtc);
