namespace Careeroc.Domain.Entities;

public sealed class ResumeVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResumeDocumentId { get; set; }
    public ResumeDocument ResumeDocument { get; set; } = null!;
    public string Summary { get; set; } = string.Empty;
    public string SkillsJson { get; set; } = "[]";
    public string ProjectHighlightsJson { get; set; } = "[]";
    public string StructuredDataJson { get; set; } = "{}";
    public string ExtractionMetadataJson { get; set; } = "{}";
    public string? EvaluationRunId { get; set; }
    public int ExperienceCount { get; set; }
    public int ProjectCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ResumeEvaluation? Evaluation { get; set; }
    public ICollection<ResumeCorrection> Corrections { get; set; } = new List<ResumeCorrection>();
}
