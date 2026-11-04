using Careeroc.Contracts.Resumes;

namespace Careeroc.Application.Abstractions;

public sealed class NullResumeFitEnricher : IResumeFitEnricher
{
    public static NullResumeFitEnricher Instance { get; } = new();

    private NullResumeFitEnricher()
    {
    }

    public Task<ResumeFitDto> EnrichAsync(
        ResumeFitDto deterministicFit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(deterministicFit);
    }
}
