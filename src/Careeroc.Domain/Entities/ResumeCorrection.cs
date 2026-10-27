namespace Careeroc.Domain.Entities;

public sealed class ResumeCorrection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResumeVersionId { get; set; }
    public ResumeVersion ResumeVersion { get; set; } = null!;
    public string FieldPath { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string CorrectedValue { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
