using System.Threading.Channels;

namespace Careeroc.Api.Background;

// Sync requests from the "Sync inbox" button. Bounded, so a burst of clicks cannot pile up
// unlimited work. A user is held from the click until their sync finishes, so two of their syncs
// never run at once; a click during a run asks for one more run after it. SyncNowWorker drains it.
public sealed class SyncNowQueue
{
    private readonly Channel<string> _channel;
    private readonly object _gate = new();
    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
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
            if (_held.Contains(userEmail))
            {
                _rerun.Add(userEmail);
                return true;
            }

            if (!_channel.Writer.TryWrite(userEmail))
            {
                return false;
            }

            _held.Add(userEmail);
            return true;
        }
    }

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    // Called when a user's sync ends, however it ends.
    public void Complete(string userEmail)
    {
        lock (_gate)
        {
            if (_rerun.Remove(userEmail) && _channel.Writer.TryWrite(userEmail))
            {
                return;
            }

            _held.Remove(userEmail);
        }
    }
}
