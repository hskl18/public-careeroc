namespace Careeroc.Application.Models;

public sealed record ScamRiskDecision(
    bool RequiresManualReview,
    string RiskLevel,
    string VisibleOutput,
    string? Reason,
    decimal Confidence,
    IReadOnlyList<string> Signals)
{
    public static ScamRiskDecision Clear { get; } = new(
        false,
        "low",
        "No obvious recruiting scam indicators were found.",
        null,
        0.40m,
        []);
}
