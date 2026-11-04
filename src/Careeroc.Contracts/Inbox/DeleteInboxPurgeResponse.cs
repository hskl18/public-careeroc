namespace Careeroc.Contracts.Inbox;

public sealed record DeleteInboxPurgeResponse(
    Guid InboxAccountId,
    string EmailAddress,
    int DeletedEmails,
    int DeletedProcessingJobs,
    int DeletedApplications,
    int DeletedReminders,
    int DeletedEvents,
    int UnlinkedEvents,
    int DeletedInboxAccounts);
