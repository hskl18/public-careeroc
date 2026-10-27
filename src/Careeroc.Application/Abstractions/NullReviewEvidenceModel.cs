using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public sealed class NullReviewEvidenceModel : IReviewEvidenceModel
{
    public static NullReviewEvidenceModel Instance { get; } = new();

    private NullReviewEvidenceModel()
    {
    }

    public Task<ReviewEvidenceModelDecision?> EvaluateAsync(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match,
        ReviewEvidenceDecision baselineDecision,
        CancellationToken cancellationToken = default,
        ModelCallContext? modelCallContext = null)
    {
        return Task.FromResult<ReviewEvidenceModelDecision?>(null);
    }
}
