namespace Careeroc.Domain.Entities;

public sealed class UserMemoryEntry
{
    // User-entered values are capped at 2000 characters by the application; system reviews with traces need more room.
    public const int MaxStoredValueLength = 32000;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Kind { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string SourceType { get; set; } = "user";
    public Guid? SourceId { get; set; }
    public double Confidence { get; set; } = 1.0;
    public string ConfirmationStatus { get; set; } = "confirmed";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
