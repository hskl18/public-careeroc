namespace Careeroc.Application.Models;

public sealed record InboxSyncCoordinatorResult(
    int EmailsCreated,
    int JobsEnqueued,
    int StaleEmailsQueued,
    int JobsProcessed,
    int RemindersRefreshed,
    int EmailsUpdated);
