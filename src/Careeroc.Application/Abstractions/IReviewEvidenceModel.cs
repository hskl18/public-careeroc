using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public interface IReviewEvidenceModel
{
    Task<ReviewEvidenceModelDecision?> EvaluateAsync(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match,
        ReviewEvidenceDecision baselineDecision,
        CancellationToken cancellationToken = default,
        ModelCallContext? modelCallContext = null);
}
