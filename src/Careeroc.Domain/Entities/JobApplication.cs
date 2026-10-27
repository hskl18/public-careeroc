using Careeroc.Domain.Enums;

namespace Careeroc.Domain.Entities;

public sealed class JobApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? RoleCanonical { get; set; }
    public string Source { get; set; } = "Email";
    public string? SourceSystem { get; set; }
    public string? Location { get; set; }
    public string? SeniorityLevel { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Applied;
    public int Priority { get; set; } = 3;
    public DateTimeOffset LastActivityAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public User User { get; set; } = null!;
    public ICollection<InboxEmail> Emails { get; set; } = new List<InboxEmail>();
    public ICollection<JobApplicationEvent> Events { get; set; } = new List<JobApplicationEvent>();
    public ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();
    public ICollection<ApplicationArtifactEvidence> ArtifactEvidence { get; set; } = new List<ApplicationArtifactEvidence>();
}
