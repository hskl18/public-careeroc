namespace Careeroc.Contracts.Dashboard;

public sealed record RecentActivityDto(
    Guid Id,
    Guid JobApplicationId,
    string Company,
    string Role,
    string Description,
    string Category,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset OccurredAtUtc,
    int ImportanceScore = 0);
