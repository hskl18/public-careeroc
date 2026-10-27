namespace Careeroc.Application.Models;

public sealed record EmailSyncResult(
    int EmailsCreated,
    int JobsEnqueued,
    string Status = EmailSyncStatuses.Success,
    string? Message = null);

public static class EmailSyncStatuses
{
    public const string Success = "success";
    public const string Skipped = "skipped";
    public const string LeaseBusy = "lease_busy";
    public const string AuthRequired = "auth_required";
    public const string Failed = "failed";
    public const string Partial = "partial";
}
