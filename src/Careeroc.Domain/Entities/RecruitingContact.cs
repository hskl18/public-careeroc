using Careeroc.Domain.Enums;

namespace Careeroc.Domain.Entities;

public sealed class RecruitingContact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? CompanyHint { get; set; }
    public string? RoleHint { get; set; }
    public RecruitingContactType? ContactType { get; set; }
    public decimal? Confidence { get; set; }
    public int MessageCount { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public User User { get; set; } = null!;
    public ICollection<InboxEmail> Emails { get; set; } = new List<InboxEmail>();
}
