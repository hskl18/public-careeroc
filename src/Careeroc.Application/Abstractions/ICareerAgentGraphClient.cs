using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public interface ICareerAgentGraphClient
{
    Task<CareerAgentGraphRunOutcome> ReviewResumeMailboxAsync(
        CareerAgentGraphReviewRequest request,
        CancellationToken cancellationToken = default);

    Task<CareerAgentGraphRunOutcome> DecideReviewAsync(
        string threadId,
        bool approved,
        CancellationToken cancellationToken = default);
}

public sealed class NullCareerAgentGraphClient : ICareerAgentGraphClient
{
    public static NullCareerAgentGraphClient Instance { get; } = new();

    private NullCareerAgentGraphClient()
    {
    }

    public Task<CareerAgentGraphRunOutcome> ReviewResumeMailboxAsync(
        CareerAgentGraphReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CareerAgentGraphRunOutcome.Skipped());
    }

    public Task<CareerAgentGraphRunOutcome> DecideReviewAsync(
        string threadId,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CareerAgentGraphRunOutcome.Skipped());
    }
}
