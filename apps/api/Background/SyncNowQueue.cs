using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Careeroc.Api.Background;

// Sync requests from the "Sync inbox" button. Bounded, so a burst of clicks cannot pile up
// unlimited work, and each user is queued once at a time; SyncNowWorker drains it.
public sealed class SyncNowQueue
{
    private readonly Channel<string> _channel;
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);

    public SyncNowQueue(int capacity)
    {
        _channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait });
    }

    // True when the user's sync is queued, now or by an earlier request; false when the queue is full.
    public bool TryEnqueue(string userEmail)
    {
        if (!_queued.TryAdd(userEmail, 0))
        {
            return true;
        }

        if (_channel.Writer.TryWrite(userEmail))
        {
            return true;
        }

        _queued.TryRemove(userEmail, out _);
        return false;
    }

    public async IAsyncEnumerable<string> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var userEmail in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            // Released before the sync runs, so a click during a long sync queues the next one.
            _queued.TryRemove(userEmail, out _);
            yield return userEmail;
        }
    }
}
