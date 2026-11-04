namespace Careeroc.Application.Models;

public sealed record GmailHistorySyncResult(
    IReadOnlyList<ExternalInboxEmail> Emails,
    string? HistoryId,
    bool RequiresFullSyncFallback = false);
