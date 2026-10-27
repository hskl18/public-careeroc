namespace Careeroc.Contracts.Emails;

public sealed class CorrectEmailRequest
{
    public string? Category { get; init; }
    public Guid? JobApplicationId { get; init; }
    public bool ClearApplicationAssignment { get; init; }
    public string? Company { get; init; }
    public string? Role { get; init; }
    public bool? ActionRequired { get; init; }
    public DateTimeOffset? DueDateUtc { get; init; }
    public bool ClearDueDate { get; init; }
    public string? Notes { get; init; }
}
