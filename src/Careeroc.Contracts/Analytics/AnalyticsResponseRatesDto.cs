namespace Careeroc.Contracts.Analytics;

public sealed record AnalyticsResponseRatesDto(
    decimal AnyResponseRate,
    decimal AutoReceiptRate,
    decimal HumanResponseRate,
    decimal AssessmentRate,
    decimal InterviewRate,
    decimal OfferRate,
    decimal RejectionRate);
