namespace Careeroc.Contracts.Inbox;

public sealed record SyncNowResponseDto(
    bool Queued,
    DateTimeOffset AttemptedAtUtc);
