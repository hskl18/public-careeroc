namespace Careeroc.Contracts.Analytics;

public sealed record AnalyticsMetricsDto(
    int ApplicationsCount,
    int UniqueCompanies,
    int RolesCount,
    decimal ReplyRate,
    decimal InterviewRate,
    decimal OfferRate,
    decimal? AvgTimeToFirstResponseHours,
    AnalyticsResponseRatesDto? ResponseRates = null);
