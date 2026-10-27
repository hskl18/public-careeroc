using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class RecruitingContactGraphService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public RecruitingContactGraphService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<RecruitingContact?> UpsertFromEmailAsync(
        InboxEmail inboxEmail,
        RecruiterIdentityDecision decision,
        ParsedRecruitingEmail parsed,
        CancellationToken cancellationToken = default)
    {
        var contactEmail = NormalizeEmail(decision.ContactEmail);
        if (contactEmail is null)
        {
            return null;
        }

        var contact = await _dbContext.RecruitingContacts
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.UserId == inboxEmail.InboxAccount.UserId &&
                    candidate.Email == contactEmail,
                cancellationToken);
        var now = _clock.UtcNow;
        if (contact is null)
        {
            contact = new RecruitingContact
            {
                UserId = inboxEmail.InboxAccount.UserId,
                Email = contactEmail,
                FirstSeenAtUtc = inboxEmail.ReceivedAtUtc,
                MessageCount = 0
            };
            _dbContext.RecruitingContacts.Add(contact);
        }

        contact.DisplayName = FirstNonEmpty(decision.ContactName, contact.DisplayName);
        contact.CompanyHint = FirstNonEmpty(NormalizeCompanyHint(parsed.Company), contact.CompanyHint);
        contact.RoleHint = FirstNonEmpty(decision.RoleHint, contact.RoleHint);
        contact.ContactType = decision.ContactType ?? contact.ContactType;
        contact.Confidence = MaxNullable(contact.Confidence, decision.Confidence);
        contact.FirstSeenAtUtc = contact.FirstSeenAtUtc <= inboxEmail.ReceivedAtUtc
            ? contact.FirstSeenAtUtc
            : inboxEmail.ReceivedAtUtc;
        contact.LastSeenAtUtc = contact.LastSeenAtUtc >= inboxEmail.ReceivedAtUtc
            ? contact.LastSeenAtUtc
            : inboxEmail.ReceivedAtUtc;
        contact.UpdatedAtUtc = now;

        if (inboxEmail.RecruitingContactId != contact.Id)
        {
            inboxEmail.RecruitingContactId = contact.Id;
            contact.MessageCount++;
        }

        return contact;
    }

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeCompanyHint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, ApplicationDefaults.UnknownCompany, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
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
}
