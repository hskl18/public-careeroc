namespace Careeroc.Contracts.Profile;

public sealed record UserProfileDto(
    string Email,
    string Name,
    string TimeZoneId,
    string ProfileIcon,
    string? Location,
    string? TargetRole,
    DateTimeOffset? WelcomeDismissedAtUtc,
    DateTimeOffset CreatedAtUtc,
    UserProfileEvaluationDto Evaluation,
    IReadOnlyList<UserMemoryEntryDto> Memory);

public sealed record UserMemoryEntryDto(
    Guid Id,
    string Kind,
    string Key,
    string Value,
    string? Summary,
    string SourceType,
    Guid? SourceId,
    double Confidence,
    string ConfirmationStatus,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);
