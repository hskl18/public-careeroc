namespace Careeroc.Contracts.Inbox;

public sealed record DeleteImportedInboxDataResponse(
    Guid InboxAccountId,
    string EmailAddress,
    int DeletedEmails,
    int DeletedProcessingJobs,
    int UnlinkedEvents,
    int PreservedApplications);
