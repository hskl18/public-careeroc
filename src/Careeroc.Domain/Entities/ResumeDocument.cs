namespace Careeroc.Domain.Entities;

public sealed class ResumeDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string SourceKind { get; set; } = "text";
    public string? SourceFileName { get; set; }
    public string SourceText { get; set; } = string.Empty;
    public string? SourceLinksJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ResumeVersion> Versions { get; set; } = new List<ResumeVersion>();
}
