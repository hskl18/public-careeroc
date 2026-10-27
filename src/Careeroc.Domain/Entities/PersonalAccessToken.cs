namespace Careeroc.Domain.Entities;

// A read-only credential a user hands to their own assistant for MCP and A2A access.
public sealed class PersonalAccessToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string TokenPrefix { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string Scope { get; set; } = "read";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
