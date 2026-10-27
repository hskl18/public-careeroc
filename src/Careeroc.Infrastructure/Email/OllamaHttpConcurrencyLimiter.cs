using Microsoft.Extensions.Options;

namespace Careeroc.Infrastructure.Email;

public sealed class OllamaHttpConcurrencyLimiter
{
    private readonly SemaphoreSlim _semaphore;

    public OllamaHttpConcurrencyLimiter(IOptions<OllamaEmailExtractionOptions> options)
    {
        var maxConcurrentRequests = Math.Clamp(options.Value.MaxConcurrentRequests, 1, 64);
        _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
