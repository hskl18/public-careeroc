using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public sealed class NullResumeVisionTextExtractor : IResumeVisionTextExtractor
{
    public static NullResumeVisionTextExtractor Instance { get; } = new();

    private NullResumeVisionTextExtractor()
    {
    }

    public Task<ResumeVisionTextExtractionDecision?> ExtractAsync(
        ResumeVisionTextExtractionRequest request,
        CancellationToken cancellationToken = default,
        ModelCallContext? modelCallContext = null)
    {
        return Task.FromResult<ResumeVisionTextExtractionDecision?>(null);
    }
}
