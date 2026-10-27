namespace Careeroc.Domain.Entities;

public sealed class ModelCallEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public Guid? InboxAccountId { get; set; }
    public Guid? InboxEmailId { get; set; }
    public string? RunId { get; set; }
    public string? CorrelationId { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public int LatencyMs { get; set; }
    public int? StatusCode { get; set; }
    public string? ErrorType { get; set; }
}
