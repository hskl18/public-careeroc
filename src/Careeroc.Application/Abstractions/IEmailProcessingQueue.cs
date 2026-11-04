using Careeroc.Domain.Entities;

namespace Careeroc.Application.Abstractions;

public interface IEmailProcessingQueue
{
    Task<int> EnqueueAsync(Guid inboxEmailId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmailProcessingJob>> DequeuePendingBatchAsync(
        int maxBatchSize,
        string? userEmail = null,
        CancellationToken cancellationToken = default);
    Task<int> CountReadyAsync(
        string? userEmail = null,
        CancellationToken cancellationToken = default);
    Task MarkCompletedAsync(
        Guid jobId,
        CancellationToken cancellationToken = default,
        DateTimeOffset? lockedAtUtc = null);
    // Puts a claimed job back without spending its attempt, when another worker must finish first.
    Task DeferAsync(
        Guid jobId,
        TimeSpan delay,
        CancellationToken cancellationToken = default,
        DateTimeOffset? lockedAtUtc = null);
    Task MarkFailedAsync(
        Guid jobId,
        string error,
        CancellationToken cancellationToken = default,
        DateTimeOffset? lockedAtUtc = null);
}
