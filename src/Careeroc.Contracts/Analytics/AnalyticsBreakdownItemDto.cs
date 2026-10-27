namespace Careeroc.Contracts.Analytics;

public sealed record AnalyticsBreakdownItemDto(
    string Label,
    int ApplicationsCount,
    decimal ReplyRate,
    decimal InterviewRate,
    decimal OfferRate,
    decimal? AvgTimeToFirstResponseHours,
    AnalyticsResponseRatesDto? ResponseRates = null);
