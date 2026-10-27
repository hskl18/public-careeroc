namespace Careeroc.Contracts.Review;

public sealed record ReviewThreadMessageDto(
    Guid Id,
    string Sender,
    string Subject,
    string Snippet,
    DateTimeOffset ReceivedAtUtc,
    string? Category,
    bool RequiresManualReview,
    bool IsCurrentEmail);
