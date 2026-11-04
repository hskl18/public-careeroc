namespace Careeroc.Domain.Entities;

public sealed class ResumeEvaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResumeVersionId { get; set; }
    public ResumeVersion ResumeVersion { get; set; } = null!;
    public int Score { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string Verdict { get; set; } = string.Empty;
    public string StrengthsJson { get; set; } = "[]";
    public string GapsJson { get; set; } = "[]";
    public string NextActionsJson { get; set; } = "[]";
    public string SectionReviewsJson { get; set; } = "[]";
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
