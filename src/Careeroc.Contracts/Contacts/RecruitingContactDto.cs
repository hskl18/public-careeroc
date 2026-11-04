namespace Careeroc.Contracts.Contacts;

public sealed record RecruitingContactListResponse(
    IReadOnlyList<RecruitingContactDto> Contacts);

public sealed record RecruitingContactDto(
    Guid Id,
    string Email,
    string? DisplayName,
    string? CompanyHint,
    string? RoleHint,
    string? ContactType,
    decimal? Confidence,
    int MessageCount,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    IReadOnlyList<RecruitingContactRecentEmailDto> RecentEmails);

public sealed record RecruitingContactRecentEmailDto(
    Guid Id,
    Guid? JobApplicationId,
    string? Company,
    string? Role,
    string Subject,
    string Sender,
    DateTimeOffset ReceivedAtUtc);

public sealed record UpdateRecruitingContactRequest(
    string? DisplayName,
    string? CompanyHint,
    string? RoleHint,
    string? ContactType,
    string? Email = null);
