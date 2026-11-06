using Careeroc.Api.Background;

namespace Careeroc.Tests;

public sealed class SyncNowQueueTests
{
    [Fact]
    public async Task EachUserIsQueuedOnceUntilTheirSyncStarts()
    {
        var queue = new SyncNowQueue(capacity: 10);

        Assert.True(queue.TryEnqueue("ada@example.com"));
        Assert.True(queue.TryEnqueue("ADA@example.com"));
        Assert.True(queue.TryEnqueue("grace@example.com"));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reader = queue.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("ada@example.com", reader.Current);
        // Once Ada's sync has started, another click queues a new one.
        Assert.True(queue.TryEnqueue("ada@example.com"));
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("grace@example.com", reader.Current);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("ada@example.com", reader.Current);
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
}
