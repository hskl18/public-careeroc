using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public interface IResumeEvaluationModel
{
    Task<ResumeModelEvaluationDecision?> EvaluateAsync(
        ResumeModelEvaluationRequest request,
        CancellationToken cancellationToken = default,
        ModelCallContext? modelCallContext = null);
}
