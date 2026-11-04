namespace Careeroc.Domain.Entities;

public sealed class ApplicationResumeFit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid JobApplicationId { get; set; }
    public JobApplication JobApplication { get; set; } = null!;
    public Guid ResumeVersionId { get; set; }
    public ResumeVersion ResumeVersion { get; set; } = null!;
    public int MatchScore { get; set; }
    public string StrengthsJson { get; set; } = "[]";
    public string GapsJson { get; set; } = "[]";
    public string Rationale { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
