using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public interface IRecruitingEmailGuidanceStore
{
    Task<string> ReadGuidanceAsync(CancellationToken cancellationToken = default);

    Task AppendFeedbackAsync(
        RecruitingEmailGuidanceFeedback feedback,
        CancellationToken cancellationToken = default);
}

