using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public interface IResumeVisionTextExtractor
{
    Task<ResumeVisionTextExtractionDecision?> ExtractAsync(
        ResumeVisionTextExtractionRequest request,
        CancellationToken cancellationToken = default,
        ModelCallContext? modelCallContext = null);
}
