using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Careeroc.Api.Background;

// Sync requests from the "Sync inbox" button. Bounded, so a burst of clicks cannot pile up
// unlimited work. A user is queued once and never runs twice at once: a click while their sync
// waits changes nothing, and a click while it runs asks for one more run after it.
public sealed class SyncNowQueue
{
    private readonly Channel<string> _channel;
    private readonly object _gate = new();
    private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rerun = new(StringComparer.OrdinalIgnoreCase);

    public SyncNowQueue(int capacity)
    {
        _channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait });
    }

    // True when the user's sync is queued or running, now or by an earlier request; false when the
    // queue is full.
    public bool TryEnqueue(string userEmail)
    {
        lock (_gate)
        {
            if (_running.Contains(userEmail))
            {
                _rerun.Add(userEmail);
                return true;
            }

            if (_queued.Contains(userEmail))
            {
                return true;
            }

            if (!_channel.Writer.TryWrite(userEmail))
            {
                return false;
            }

            _queued.Add(userEmail);
            return true;
        }
    }

    public async IAsyncEnumerable<string> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var userEmail in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            lock (_gate)
            {
                _queued.Remove(userEmail);
                _running.Add(userEmail);
            }

            yield return userEmail;
        }
    }

    // Called when a user's sync ends, however it ends.
    public void Complete(string userEmail)
    {
        lock (_gate)
        {
            _running.Remove(userEmail);
            if (_rerun.Remove(userEmail) && _channel.Writer.TryWrite(userEmail))
            {
                _queued.Add(userEmail);
            }
        }
    }
}
