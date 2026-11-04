using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public sealed class NullRecruitingEmailGuidanceStore : IRecruitingEmailGuidanceStore
{
    public static NullRecruitingEmailGuidanceStore Instance { get; } = new();

    private NullRecruitingEmailGuidanceStore()
    {
    }

    public Task<string> ReadGuidanceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }

    public Task AppendFeedbackAsync(
        RecruitingEmailGuidanceFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

