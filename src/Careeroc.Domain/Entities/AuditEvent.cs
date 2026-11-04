namespace Careeroc.Domain.Entities;

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public string? TargetEmail { get; set; }
    public string? ResourceType { get; set; }
    public Guid? ResourceId { get; set; }
    public string? Method { get; set; }
    public string? Path { get; set; }
    public string? RemoteIp { get; set; }
    public string? Detail { get; set; }
}
