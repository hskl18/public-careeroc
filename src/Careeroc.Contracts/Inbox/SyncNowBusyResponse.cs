namespace Careeroc.Contracts.Inbox;

// Error is "sync_now_rate_limited" within 30 seconds of the last request, or "sync_now_busy" when
// the sync queue is full.
public sealed record SyncNowBusyResponse(
    string Error,
    int RetryAfterSeconds);
