using Careeroc.Api.Background;

namespace Careeroc.Tests;

public sealed class SyncNowQueueTests
{
    [Fact]
    public async Task AClickWhileQueuedChangesNothingAndAClickWhileRunningQueuesOneRerun()
    {
        var queue = new SyncNowQueue(capacity: 10);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reader = queue.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();

        Assert.True(queue.TryEnqueue("ada@example.com"));
        // Still waiting in the queue: a second click asks for nothing more.
        Assert.True(queue.TryEnqueue("ADA@example.com"));
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("ada@example.com", reader.Current);
        queue.Complete("ada@example.com");
        Assert.False(await HasNextWithinAsync(queue));

        Assert.True(queue.TryEnqueue("ada@example.com"));
        Assert.True(await reader.MoveNextAsync());
        // Running now: two clicks ask for one more run after it, not a parallel one.
        Assert.True(queue.TryEnqueue("ada@example.com"));
        Assert.True(queue.TryEnqueue("ada@example.com"));
        queue.Complete("ada@example.com");
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("ada@example.com", reader.Current);
        queue.Complete("ada@example.com");
        Assert.False(await HasNextWithinAsync(queue));
        await reader.DisposeAsync();
    }

    [Fact]
    public void AFullQueueRefusesNewUsers()
    {
        var queue = new SyncNowQueue(capacity: 2);

        Assert.True(queue.TryEnqueue("one@example.com"));
        Assert.True(queue.TryEnqueue("two@example.com"));
        Assert.False(queue.TryEnqueue("three@example.com"));
        // A refused user is not left marked as queued.
        Assert.False(queue.TryEnqueue("three@example.com"));
    }

    private static async Task<bool> HasNextWithinAsync(SyncNowQueue queue)
    {
        using var shortWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await using var probe = queue.ReadAllAsync(shortWait.Token).GetAsyncEnumerator();
            return await probe.MoveNextAsync();
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
