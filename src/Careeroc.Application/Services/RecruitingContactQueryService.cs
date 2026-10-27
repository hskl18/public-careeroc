using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Contacts;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class RecruitingContactQueryService
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;
    private const int RecentEmailLimit = 5;

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public RecruitingContactQueryService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<RecruitingContactListResponse> GetAsync(
        string? userEmail,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.Email == resolvedUserEmail)
            .Select(candidate => new { candidate.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return new RecruitingContactListResponse([]);
        }

        var contacts = await _dbContext.RecruitingContacts
            .AsNoTracking()
            .Where(contact => contact.UserId == user.Id)
            .OrderByDescending(contact => contact.LastSeenAtUtc)
            .Take(effectiveLimit)
            .Select(
                contact => new ContactRecord(
                    contact.Id,
                    contact.Email,
                    contact.DisplayName,
                    contact.CompanyHint,
                    contact.RoleHint,
                    contact.ContactType.HasValue ? contact.ContactType.Value.ToString() : null,
                    contact.Confidence,
                    contact.MessageCount,
                    contact.FirstSeenAtUtc,
                    contact.LastSeenAtUtc))
            .ToListAsync(cancellationToken);
        if (contacts.Count == 0)
        {
            return new RecruitingContactListResponse([]);
        }

        var contactIds = contacts.Select(contact => contact.Id).ToHashSet();
        var emailRecords = await _dbContext.InboxEmails
            .AsNoTracking()
            .Where(email =>
                email.RecruitingContactId.HasValue &&
                contactIds.Contains(email.RecruitingContactId.Value))
            .OrderByDescending(email => email.ReceivedAtUtc)
            .Select(
                email => new ContactEmailRecord(
                    email.RecruitingContactId!.Value,
                    email.Id,
                    email.JobApplicationId,
                    email.JobApplication == null ? null : email.JobApplication.Company,
                    email.JobApplication == null ? null : email.JobApplication.Role,
                    email.Subject,
                    email.Sender,
                    email.ReceivedAtUtc))
            .ToListAsync(cancellationToken);

        var emailsByContact = emailRecords
            .GroupBy(email => email.ContactId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Take(RecentEmailLimit)
                    .Select(
                        email => new RecruitingContactRecentEmailDto(
                            email.Id,
                            email.JobApplicationId,
                            email.Company,
                            email.Role,
                            email.Subject,
                            email.Sender,
                            email.ReceivedAtUtc))
                    .ToList() as IReadOnlyList<RecruitingContactRecentEmailDto>);

        return new RecruitingContactListResponse(
            contacts
                .Select(
                    contact => new RecruitingContactDto(
                        contact.Id,
                        contact.Email,
                        contact.DisplayName,
                        contact.CompanyHint,
                        contact.RoleHint,
                        contact.ContactType,
                        contact.Confidence,
                        contact.MessageCount,
                        contact.FirstSeenAtUtc,
                        contact.LastSeenAtUtc,
                        emailsByContact.GetValueOrDefault(contact.Id) ?? []))
                .ToList());
    }

    public async Task<RecruitingContactDto?> UpdateAsync(
        string? userEmail,
        Guid contactId,
        UpdateRecruitingContactRequest request,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var user = await _dbContext.Users
            .Where(candidate => candidate.Email == resolvedUserEmail)
            .Select(candidate => new { candidate.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var contact = await _dbContext.RecruitingContacts
            .Where(candidate => candidate.UserId == user.Id && candidate.Id == contactId)
            .FirstOrDefaultAsync(cancellationToken);
        if (contact is null)
        {
            return null;
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        if (!string.IsNullOrWhiteSpace(normalizedEmail) &&
            !string.Equals(normalizedEmail, contact.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existingContact = await _dbContext.RecruitingContacts
                .FirstOrDefaultAsync(
                    candidate =>
                        candidate.UserId == user.Id &&
                        candidate.Email == normalizedEmail,
                    cancellationToken);
            if (existingContact is not null)
            {
                return await MergeContactIntoExistingAsync(
                    contact,
                    existingContact,
                    request,
                    cancellationToken);
            }

            contact.Email = normalizedEmail;
        }

        // PATCH: a field the client leaves out keeps its value; an empty string clears it.
        if (request.DisplayName is not null)
        {
            contact.DisplayName = NormalizeOptionalText(request.DisplayName);
        }

        if (request.CompanyHint is not null)
        {
            contact.CompanyHint = NormalizeOptionalText(request.CompanyHint);
        }

        if (request.RoleHint is not null)
        {
            contact.RoleHint = NormalizeOptionalText(request.RoleHint);
        }

        if (request.ContactType is not null)
        {
            contact.ContactType = ParseContactType(request.ContactType);
        }

        contact.UpdatedAtUtc = _clock.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RecruitingContactDto(
            contact.Id,
            contact.Email,
            contact.DisplayName,
            contact.CompanyHint,
            contact.RoleHint,
            contact.ContactType.HasValue ? contact.ContactType.Value.ToString() : null,
            contact.Confidence,
            contact.MessageCount,
            contact.FirstSeenAtUtc,
            contact.LastSeenAtUtc,
            []);
    }

    private async Task<RecruitingContactDto> MergeContactIntoExistingAsync(
        Domain.Entities.RecruitingContact source,
        Domain.Entities.RecruitingContact target,
        UpdateRecruitingContactRequest request,
        CancellationToken cancellationToken)
    {
        var linkedEmails = await _dbContext.InboxEmails
            .Where(email => email.RecruitingContactId == source.Id)
            .ToListAsync(cancellationToken);

        foreach (var email in linkedEmails)
        {
            email.RecruitingContactId = target.Id;
        }

        target.DisplayName = FirstNonEmpty(request.DisplayName, target.DisplayName, source.DisplayName);
        target.CompanyHint = FirstNonEmpty(request.CompanyHint, target.CompanyHint, source.CompanyHint);
        target.RoleHint = FirstNonEmpty(request.RoleHint, target.RoleHint, source.RoleHint);
        target.ContactType = ParseContactType(request.ContactType) ?? target.ContactType ?? source.ContactType;
        target.Confidence = MaxNullable(target.Confidence, source.Confidence);
        target.MessageCount += linkedEmails.Count;
        target.FirstSeenAtUtc = Min(target.FirstSeenAtUtc, source.FirstSeenAtUtc);
        target.LastSeenAtUtc = Max(target.LastSeenAtUtc, source.LastSeenAtUtc);
        target.UpdatedAtUtc = _clock.UtcNow;

        _dbContext.RecruitingContacts.Remove(source);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RecruitingContactDto(
            target.Id,
            target.Email,
            target.DisplayName,
            target.CompanyHint,
            target.RoleHint,
            target.ContactType.HasValue ? target.ContactType.Value.ToString() : null,
            target.Confidence,
            target.MessageCount,
            target.FirstSeenAtUtc,
            target.LastSeenAtUtc,
            []);
    }

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        if (!trimmed.Contains('@', StringComparison.Ordinal) || trimmed.Length > 320)
        {
            throw new InvalidOperationException("Recruiting contact email must be a valid email address.");
        }

        return trimmed;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }

    private static RecruitingContactType? ParseContactType(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var normalized = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<RecruitingContactType>(normalized, ignoreCase: true, out var contactType))
        {
            return contactType;
        }

        throw new InvalidOperationException("Unsupported recruiting contact type.");
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return NormalizeOptionalText(value);
            }
        }

        return null;
    }

    private static decimal? MaxNullable(decimal? first, decimal? second)
    {
        return first.HasValue && second.HasValue
            ? Math.Max(first.Value, second.Value)
            : first ?? second;
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second)
    {
        return first <= second ? first : second;
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second)
    {
        return first >= second ? first : second;
    }

    private sealed record ContactRecord(
        Guid Id,
        string Email,
        string? DisplayName,
        string? CompanyHint,
        string? RoleHint,
        string? ContactType,
        decimal? Confidence,
        int MessageCount,
        DateTimeOffset FirstSeenAtUtc,
        DateTimeOffset LastSeenAtUtc);

    private sealed record ContactEmailRecord(
        Guid ContactId,
        Guid Id,
        Guid? JobApplicationId,
        string? Company,
        string? Role,
        string Subject,
        string Sender,
        DateTimeOffset ReceivedAtUtc);
}
