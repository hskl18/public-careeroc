namespace Careeroc.Domain.Entities;

public sealed class UserSoulProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string NarrativeTone { get; set; } = "clear, direct, evidence-backed";
    public string? CareerNarrative { get; set; }
    public string? CommunicationPreferencesJson { get; set; }
    public string RenderedSoulMarkdown { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
