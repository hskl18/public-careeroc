namespace Careeroc.Contracts.Analytics;

public sealed record AnalyticsResponse(
    AnalyticsMetricsDto Metrics,
    IReadOnlyList<AnalyticsBreakdownItemDto> CompanyBreakdown,
    IReadOnlyList<AnalyticsBreakdownItemDto> RoleBreakdown,
    IReadOnlyList<AnalyticsFunnelStageDto>? Funnel = null,
    IReadOnlyList<AnalyticsBreakdownItemDto>? SourceBreakdown = null,
    IReadOnlyList<AnalyticsBreakdownItemDto>? LocationBreakdown = null,
    IReadOnlyList<AnalyticsBreakdownItemDto>? SeniorityBreakdown = null,
    IReadOnlyList<AnalyticsInsightDto>? Insights = null,
    DateTimeOffset? LastSyncAtUtc = null);
