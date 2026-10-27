namespace Careeroc.Contracts.Inbox;

public sealed record InboxAccountSetupDto(
    Guid Id,
    string Provider,
    string ProviderLabel,
    string EmailAddress,
    string SetupMode,
    string ConnectionStatus,
    string NextStep,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSyncedAtUtc,
    int? BackfillMonths,
    bool BackfillAllHistory,
    DateTimeOffset? InitialBackfillCompletedAtUtc,
    bool? IsValid = null,
    string? InvalidReason = null);
