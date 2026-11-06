using System.Threading.RateLimiting;

namespace Careeroc.Api.Security;

// The global limiter runs after authentication so it can partition by user, which leaves rejected
// credentials unthrottled. This counts only failures, per client address and claimed subject, and is
// checked before any signature work or token lookup. Every browser request reaches the API from the
// web host's one address, so signed requests are counted per claimed user as well: one user's
// failures must not lock everyone out.
internal sealed class FailedAuthThrottle : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;
    // A looser cap per address alone, so changing the claimed email on every try does not reset it.
    private readonly PartitionedRateLimiter<string> _addressLimiter;

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
        _addressLimiter = PartitionedRateLimiter.Create<string, string>(
            address => RateLimitPartition.GetFixedWindowLimiter(
                address,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.AnonymousPermitLimit * 10,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0
                }));
    }

    public async Task<bool> RejectIfExhaustedAsync(HttpContext context, string? subject = null)
    {
        // Asking for zero permits reports whether any remain, without spending one.
        using var lease = _limiter.AttemptAcquire(PartitionKey(context, subject), 0);
        using var addressLease = _addressLimiter.AttemptAcquire(ApiRateLimitPartitioner.GetRemoteIp(context), 0);
        if (lease.IsAcquired && addressLease.IsAcquired)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(new { error = "Too many failed attempts. Retry later." });
        return true;
    }

    public void RecordFailure(HttpContext context, string? subject = null)
    {
        using var lease = _limiter.AttemptAcquire(PartitionKey(context, subject));
        using var addressLease = _addressLimiter.AttemptAcquire(ApiRateLimitPartitioner.GetRemoteIp(context));
    }

    // Cut to an email's maximum length, so a caller cannot make the partition keys arbitrarily large.
    private static string PartitionKey(HttpContext context, string? subject)
    {
        var claimed = (subject ?? string.Empty).Trim().ToLowerInvariant();
        return $"{ApiRateLimitPartitioner.GetRemoteIp(context)}|{claimed[..Math.Min(claimed.Length, 320)]}";
    }

    public void Dispose()
    {
        _limiter.Dispose();
        _addressLimiter.Dispose();
    }
}
