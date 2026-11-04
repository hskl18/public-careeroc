namespace Careeroc.Contracts.Dashboard;

public sealed record InboxOverviewDto(
    string ConnectedAddress,
    int SyncedEmails,
    int ProcessedEmails,
    DateTimeOffset? LastSyncAtUtc,
    int PendingAnalysisEmails = 0,
    int ProcessingAnalysisEmails = 0,
    int FailedAnalysisEmails = 0,
    DateTimeOffset? LastAnalysisCompletedAtUtc = null,
    string? LastAnalysisError = null,
    int ScannedCount = 0,
    int? TotalEstimate = null,
    int RecognizedApplications = 0,
    string BackfillStatus = "idle");
