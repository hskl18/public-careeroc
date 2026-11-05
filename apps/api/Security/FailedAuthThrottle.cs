using System.Threading.RateLimiting;

namespace Careeroc.Api.Security;

// The global limiter runs after authentication so it can partition by user, which leaves rejected
// credentials unthrottled. This counts only failures, per client address, and is checked before any
// signature work or token lookup.
internal sealed class FailedAuthThrottle : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public FailedAuthThrottle(ApiRateLimitOptions options)
    {
        _limiter = PartitionedRateLimiter.Create<string, string>(
            address => RateLimitPartition.GetFixedWindowLimiter(
                address,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.AnonymousPermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0
                }));
    }

    public async Task<bool> RejectIfExhaustedAsync(HttpContext context)
    {
        // Asking for zero permits reports whether any remain, without spending one.
        using var lease = _limiter.AttemptAcquire(ApiRateLimitPartitioner.GetRemoteIp(context), 0);
        if (lease.IsAcquired)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(new { error = "Too many failed attempts. Retry later." });
        return true;
    }

    public void RecordFailure(HttpContext context)
    {
        using var lease = _limiter.AttemptAcquire(ApiRateLimitPartitioner.GetRemoteIp(context));
    }

    public void Dispose() => _limiter.Dispose();
}
