namespace Careeroc.Contracts.Analytics;

public sealed record AnalyticsFunnelStageDto(
    string Key,
    string Label,
    int Count,
    decimal ContinuationRateFromPreviousStage);
