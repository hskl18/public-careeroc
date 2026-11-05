namespace Careeroc.Contracts.Review;

using Careeroc.Contracts.Agents;

public sealed record ManualReviewResponse(
    IReadOnlyList<ReviewEmailDto> Emails,
    IReadOnlyList<ReviewApplicationDto> Applications,
    IReadOnlyList<string> CategoryOptions,
    IReadOnlyList<string> StatusOptions,
    int TotalWaiting,
    int TotalUnassigned);

