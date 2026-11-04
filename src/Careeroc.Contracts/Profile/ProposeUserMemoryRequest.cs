namespace Careeroc.Contracts.Profile;

public sealed record ProposeUserMemoryRequest(
    string Kind,
    string Key,
    string Value,
    string? Summary,
    string SourceType,
    Guid? SourceId,
    double? Confidence,
    DateTimeOffset? ExpiresAtUtc);
