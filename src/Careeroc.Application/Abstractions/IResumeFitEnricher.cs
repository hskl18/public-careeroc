using Careeroc.Contracts.Resumes;

namespace Careeroc.Application.Abstractions;

public interface IResumeFitEnricher
{
    Task<ResumeFitDto> EnrichAsync(
        ResumeFitDto deterministicFit,
        CancellationToken cancellationToken = default);
}
