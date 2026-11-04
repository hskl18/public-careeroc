using Careeroc.Domain.Enums;

namespace Careeroc.Domain.Entities;

public sealed class EmailProcessingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InboxEmailId { get; set; }
    public EmailProcessingStatus Status { get; set; } = EmailProcessingStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset AvailableAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LockedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? RunId { get; set; }
    public string? CorrelationId { get; set; }
    public string? LastError { get; set; }
    public string? DeadLetterReason { get; set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public InboxEmail InboxEmail { get; set; } = null!;
}
