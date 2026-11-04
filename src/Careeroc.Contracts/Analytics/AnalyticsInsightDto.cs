namespace Careeroc.Contracts.Analytics;

public sealed record AnalyticsInsightDto(
    string Id,
    string Tone,
    string Headline,
    string Body,
    string? DrillHref = null);
