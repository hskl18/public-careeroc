namespace Careeroc.Contracts.Profile;

public sealed record UserAgentContextSnapshotDto(
    DateTimeOffset GeneratedAtUtc,
    string MemoryMarkdown,
    int MemoryEntryCount,
    DateTimeOffset? LatestMemoryUpdatedAtUtc);
