namespace Careeroc.Contracts.Profile;

public sealed record UserAgentContextSnapshotDto(
    DateTimeOffset GeneratedAtUtc,
    string MemoryMarkdown,
    string SoulMarkdown,
    int MemoryEntryCount,
    DateTimeOffset? LatestMemoryUpdatedAtUtc,
    DateTimeOffset? SoulUpdatedAtUtc);
