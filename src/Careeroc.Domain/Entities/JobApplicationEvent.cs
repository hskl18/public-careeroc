namespace Careeroc.Domain.Entities;

public sealed class JobApplicationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobApplicationId { get; set; }
    public Guid? InboxEmailId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset EventTimeUtc { get; set; }
    public string ExtractedDataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public JobApplication JobApplication { get; set; } = null!;
    public InboxEmail? InboxEmail { get; set; }
}
