namespace Careeroc.Application.Abstractions;

// A named lease that only one worker across all API instances holds at a time; null when busy.
public interface ILeaseManager
{
    Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken cancellationToken = default);
}

public sealed class NoopLeaseManager : ILeaseManager
{
    public static readonly NoopLeaseManager Instance = new();

    private NoopLeaseManager()
    {
    }

    public Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IAsyncDisposable?>(NoopLease.Instance);
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public static readonly NoopLease Instance = new();

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
