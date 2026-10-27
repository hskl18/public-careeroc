namespace Careeroc.Domain.Entities;

public sealed class InboxAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string? OAuthMetadataJson { get; set; }
    public string? AccessTokenEncrypted { get; set; }
    public string? RefreshTokenEncrypted { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public User User { get; set; } = null!;
    public ICollection<InboxEmail> Emails { get; set; } = new List<InboxEmail>();
}
