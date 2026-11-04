using Careeroc.Domain;

namespace Careeroc.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = UserRoleNames.User;
    public string TimeZoneId { get; set; } = "America/Los_Angeles";
    public string ProfileIcon { get; set; } = "inbox-blue";
    public string? Location { get; set; }
    public string? TargetRole { get; set; }
    public DateTimeOffset? WelcomeDismissedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<InboxAccount> InboxAccounts { get; set; } = new List<InboxAccount>();
    public ICollection<RecruitingContact> RecruitingContacts { get; set; } = new List<RecruitingContact>();
    public ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();
    public ICollection<ApplicationArtifactEvidence> ApplicationArtifactEvidence { get; set; } = new List<ApplicationArtifactEvidence>();
    public ICollection<UserMemoryEntry> MemoryEntries { get; set; } = new List<UserMemoryEntry>();
    public UserSoulProfile? SoulProfile { get; set; }
    public ICollection<ResumeDocument> ResumeDocuments { get; set; } = new List<ResumeDocument>();
}
