namespace Careeroc.Application.Models;

public sealed record UserMemoryRetentionCleanupResult(
    int RemovedMemoryEntries,
    int RemovedExpiredMemoryEntries,
    int RemovedRejectedMemoryEntries,
    DateTimeOffset? RejectedCutoffUtc);
