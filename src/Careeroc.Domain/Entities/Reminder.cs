using Careeroc.Domain.Enums;

namespace Careeroc.Domain.Entities;

public sealed class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobApplicationId { get; set; }
    public ReminderType Type { get; set; } = ReminderType.FollowUp;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset DueAtUtc { get; set; }
    public ReminderStatus Status { get; set; } = ReminderStatus.Open;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EvidenceRefreshedAtUtc { get; set; }
    public JobApplication JobApplication { get; set; } = null!;
}
