using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;

namespace Careeroc.Api.Endpoints;

// Dashboard responses are cached per user for a few seconds. Any successful write by that user moves
// their generation on, so the next read after Mark done or a correction is fresh instead of the copy
// cached before it. The counter outlives every cached response (at most 120 seconds).
internal static class DashboardCacheGeneration
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static long Current(IMemoryCache cache, string userEmail) => Counter(cache, userEmail).Value;

    public static void Advance(IMemoryCache cache, string userEmail) =>
        Interlocked.Increment(ref Counter(cache, userEmail).Value);

    private static StrongBox<long> Counter(IMemoryCache cache, string userEmail) =>
        cache.GetOrCreate(
            $"dashboard-generation|{userEmail.Trim().ToLowerInvariant()}",
            entry =>
            {
                entry.SlidingExpiration = Lifetime;
                return new StrongBox<long>();
            })!;
}
