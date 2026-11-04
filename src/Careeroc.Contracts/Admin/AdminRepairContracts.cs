namespace Careeroc.Contracts.Admin;

public sealed record AdminRepairRequest(string? Confirmation);

public sealed record AdminRetryEmailJobResponse(
    Guid JobId,
    Guid InboxEmailId,
    string Status,
    int Attempts);
