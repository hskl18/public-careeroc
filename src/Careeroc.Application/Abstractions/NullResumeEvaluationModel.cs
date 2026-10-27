using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public sealed class NullResumeEvaluationModel : IResumeEvaluationModel
{
    public static NullResumeEvaluationModel Instance { get; } = new();

    private NullResumeEvaluationModel()
    {
    }

    public Task<ResumeModelEvaluationDecision?> EvaluateAsync(
        ResumeModelEvaluationRequest request,
        CancellationToken cancellationToken = default,
        ModelCallContext? modelCallContext = null)
    {
        return Task.FromResult<ResumeModelEvaluationDecision?>(null);
    }
}
