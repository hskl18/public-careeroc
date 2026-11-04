namespace Careeroc.Contracts.Development;

public sealed record BootstrapResultDto(
    int EmailsCreated,
    int JobsEnqueued,
    int JobsProcessed,
    int ApplicationsCreated,
    int ApplicationsUpdated,
    int RemindersCreated);
