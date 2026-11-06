using Careeroc.Infrastructure.Email;

namespace Careeroc.Tests;

// The advisory lock that keeps two workers from creating the same application or syncing one inbox
// twice; the in-memory provider takes the no-op path, so only PostgreSQL can show it holds.
public sealed class DatabaseLeaseManagerPostgresTests
{
    [PostgresFact]
    public async Task ALeaseIsExclusiveAcrossConnectionsUntilItIsReleased()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var firstWorker = database.CreateContext();
        await using var secondWorker = database.CreateContext();
        var first = new DatabaseLeaseManager(firstWorker);
        var second = new DatabaseLeaseManager(secondWorker);

        var held = await first.TryAcquireAsync("inbox-sync:abc");
        Assert.NotNull(held);
        Assert.Null(await second.TryAcquireAsync("inbox-sync:abc"));
        // A different resource is a different lock.
        await using (var other = await second.TryAcquireAsync("inbox-sync:def"))
        {
            Assert.NotNull(other);
        }

        await held!.DisposeAsync();
        await using var reacquired = await second.TryAcquireAsync("inbox-sync:abc");
        Assert.NotNull(reacquired);
    }
}
