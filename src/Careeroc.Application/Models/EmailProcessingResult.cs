namespace Careeroc.Application.Models;

public sealed record EmailProcessingResult(
    int JobsProcessed,
    int ApplicationsCreated,
    int ApplicationsUpdated,
    int RemindersCreated);
