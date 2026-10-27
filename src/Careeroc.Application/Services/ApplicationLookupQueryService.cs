using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Applications;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class ApplicationLookupQueryService
{
    private const int DefaultLimit = 8;
    private const int MaxLimit = 25;

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public ApplicationLookupQueryService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<ApplicationLookupResponse> SearchAsync(
        string? userEmail,
        string? query,
        int? limit = null,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var normalizedQuery = NormalizeQuery(query);
        var boundedLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var applicationsQuery = _dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.User.Email == resolvedUserEmail);

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            applicationsQuery = applicationsQuery.Where(
                application =>
                    application.Company.ToLower().Contains(normalizedQuery) ||
                    application.Role.ToLower().Contains(normalizedQuery) ||
                    (application.RoleCanonical != null && application.RoleCanonical.ToLower().Contains(normalizedQuery)) ||
                    (application.SourceSystem != null && application.SourceSystem.ToLower().Contains(normalizedQuery)) ||
                    application.Source.ToLower().Contains(normalizedQuery) ||
                    (application.Location != null && application.Location.ToLower().Contains(normalizedQuery)));
        }

        var applications = await applicationsQuery
            .OrderByDescending(application => application.LastActivityAtUtc)
            .Take(string.Equals(sort, "urgency", StringComparison.OrdinalIgnoreCase) ? 100 : boundedLimit)
            .ToListAsync(cancellationToken);
        var applicationIds = applications.Select(application => application.Id).ToHashSet();
        var nowUtc = _clock.UtcNow;
        var nextReminders = await _dbContext.Reminders
            .AsNoTracking()
            .Where(reminder =>
                applicationIds.Contains(reminder.JobApplicationId) &&
                reminder.Status == ReminderStatus.Open)
            .GroupBy(reminder => reminder.JobApplicationId)
            .Select(group => new
            {
                JobApplicationId = group.Key,
                DueAtUtc = group.Min(reminder => reminder.DueAtUtc)
            })
            .ToDictionaryAsync(row => row.JobApplicationId, row => row.DueAtUtc, cancellationToken);

        var itemsQuery = applications
            .Select(
                application =>
                {
                    nextReminders.TryGetValue(application.Id, out var nextActionDueAtUtc);
                    DateTimeOffset? nextActionDue = nextActionDueAtUtc == default ? null : nextActionDueAtUtc;
                    return new ApplicationLookupItemDto(
                        application.Id,
                        application.Company,
                        application.Role,
                        application.Status.ToString(),
                        application.LastActivityAtUtc,
                        application.SourceSystem ?? application.Source,
                        application.Location,
                        CalculateUrgencyScore(
                            application.Status,
                            application.LastActivityAtUtc,
                            nextActionDue,
                            nowUtc),
                        nextActionDue);
                });
        var items = (string.Equals(sort, "urgency", StringComparison.OrdinalIgnoreCase)
                ? itemsQuery.OrderByDescending(item => item.UrgencyScore).ThenByDescending(item => item.LastActivityAtUtc)
                : itemsQuery)
            .Take(boundedLimit)
            .ToList();

        return new ApplicationLookupResponse(items);
    }

    private static string? NormalizeQuery(string? query)
    {
        var normalized = query?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static int CalculateUrgencyScore(
        ApplicationStatus status,
        DateTimeOffset lastActivityAtUtc,
        DateTimeOffset? nextActionDueAtUtc,
        DateTimeOffset nowUtc)
    {
        var score = 0;
        if (nextActionDueAtUtc.HasValue)
        {
            var dueIn = nextActionDueAtUtc.Value - nowUtc;
            score += dueIn <= TimeSpan.FromHours(24)
                ? 100
                : dueIn <= TimeSpan.FromHours(72)
                    ? 60
                    : 0;
        }

        if (status is ApplicationStatus.OaPending or ApplicationStatus.Interviewing or ApplicationStatus.Offer)
        {
            score += 40;
        }

        if (status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed &&
            lastActivityAtUtc <= nowUtc.AddDays(-7))
        {
            score += 20;
        }

        if (status is ApplicationStatus.Rejected or ApplicationStatus.Closed)
        {
            score -= 10;
        }

        return Math.Clamp(score, 0, 200);
    }

}
