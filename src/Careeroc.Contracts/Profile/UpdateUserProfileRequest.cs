namespace Careeroc.Contracts.Profile;

public sealed record UpdateUserProfileRequest(
    string? Name,
    string? TimeZoneId,
    string? Location,
    string? TargetRole,
    string? ProfileIcon = null);
