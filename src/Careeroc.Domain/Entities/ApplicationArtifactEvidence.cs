namespace Careeroc.Domain.Entities;

public sealed class ApplicationArtifactEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid JobApplicationId { get; set; }
    public string ArtifactKind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string? SourceLabel { get; set; }
    public string SafeTitle { get; set; } = string.Empty;
    public string ExtractedFactType { get; set; } = string.Empty;
    public string ExtractedFactText { get; set; } = string.Empty;
    public DateTimeOffset? NormalizedDateUtc { get; set; }
    public decimal Confidence { get; set; }
    public string SourceSnippet { get; set; } = string.Empty;
    public string? ModelPath { get; set; }
    public string Purpose { get; set; } = "artifact_evidence_extraction";
    public string EvidenceSource { get; set; } = string.Empty;
    public string ReviewGateResult { get; set; } = string.Empty;
    public string FallbackPath { get; set; } = string.Empty;
    public bool RequiresReview { get; set; }
    public string? ReviewReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public User User { get; set; } = null!;
    public JobApplication JobApplication { get; set; } = null!;
}
