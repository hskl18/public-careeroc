using Careeroc.Domain.Enums;

namespace Careeroc.Domain.Entities;

public sealed class InboxEmail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InboxAccountId { get; set; }
    public Guid? JobApplicationId { get; set; }
    public Guid? RecruitingContactId { get; set; }
    public string ExternalMessageId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public string RawBody { get; set; } = string.Empty;
    public string ParsedBody { get; set; } = string.Empty;
    public bool IsOutbound { get; set; }
    public EmailCategory? Category { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactRoleHint { get; set; }
    public RecruitingContactType? ContactType { get; set; }
    public bool ActionRequired { get; set; }
    public DateTimeOffset? DueDateUtc { get; set; }
    public string? ExtractedCompany { get; set; }
    public string? ExtractedRole { get; set; }
    public decimal? ClassificationConfidence { get; set; }
    public decimal? MatchingConfidence { get; set; }
    public bool RequiresManualReview { get; set; }
    public string? ReviewReason { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ProcessingNotes { get; set; }
    public string? ProcessingTraceJson { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public InboxAccount InboxAccount { get; set; } = null!;
    public JobApplication? JobApplication { get; set; }
    public RecruitingContact? RecruitingContact { get; set; }
    public ICollection<JobApplicationEvent> Events { get; set; } = new List<JobApplicationEvent>();
}
