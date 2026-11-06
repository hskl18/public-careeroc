using System.Globalization;
using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Applications;
using Careeroc.Contracts.Dashboard;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

// The dashboard read model. This file runs the query in order: load the rows (.Loading.cs), decide
// which applications each view shows (.Views.cs), then build the response (.Projections.cs).
public sealed partial class DashboardQueryService
{
    private const int RecentActivityLimit = 50;
    private const int RecentActivityFetchLimit = 250;
    private const int LinkedEmailFetchLimit = 2000;
    private static readonly TimeSpan PendingActionOverdueWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan PendingActionUpcomingWindow = TimeSpan.FromDays(14);

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public DashboardQueryService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public Task<DashboardResponse> GetDashboardAsync(
        string? userEmail,
        CancellationToken cancellationToken)
    {
        return GetDashboardAsync(userEmail, null, cancellationToken);
    }

    public async Task<DashboardResponse> GetDashboardAsync(
        string? userEmail = null,
        DashboardQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = CareerocTelemetry.ActivitySource.StartActivity("dashboard.query");
        activity?.SetTag("careeroc.user", CareerocTelemetry.UserKey(userEmail));
        activity?.SetTag("dashboard.view", options?.View);

        options ??= DashboardQueryOptions.Empty;
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(candidate => candidate.Email == resolvedUserEmail)
            .Select(candidate => new DashboardUserRecord(candidate.Id, candidate.Email))
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return EmptyDashboard(resolvedUserEmail);
        }

        var nowUtc = _clock.UtcNow;
        var data = await LoadDashboardDataAsync(user, nowUtc, cancellationToken);

        // Index the linked emails by application once; every later step reads from these.
        var emailsByApplication = data.VisibleEmails
            .Where(email => email.JobApplicationId.HasValue)
            .GroupBy(email => email.JobApplicationId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var lastOutboundReplyAtUtc = data.VisibleEmails
            .Where(email => email.JobApplicationId.HasValue && email.IsOutbound)
            .GroupBy(email => email.JobApplicationId!.Value)
            .ToDictionary(group => group.Key, group => (DateTimeOffset?)group.Max(email => email.ReceivedAtUtc));
        var reviewStates = BuildReviewStates(data.Applications, emailsByApplication);
        var visibleApplicationIds = FindVisibleApplicationIds(data);
        var applications = data.Applications
            .Where(application => visibleApplicationIds.Contains(application.Id))
            .ToList();

        var events = await LoadRecentEventsAsync(visibleApplicationIds, cancellationToken);
        var actionReminders = BuildActionReminders(data.Reminders, visibleApplicationIds, emailsByApplication);
        var applicationIdsWithOpenReminder = actionReminders
            .Select(record => record.Reminder.JobApplicationId)
            .ToHashSet();
        var nextReminderByApplicationId = actionReminders
            .GroupBy(record => record.Reminder.JobApplicationId)
            .ToDictionary(group => group.Key, group => group.OrderBy(record => record.Reminder.DueAtUtc).First());
        var latestEvidenceByApplication = BuildLatestEvidenceByApplication(events, data.VisibleEmails);

        // Counts cover every visible application; the view, filters, and page narrow only the list.
        var metrics = BuildMetrics(
            applications,
            actionReminders.Count,
            reviewStates,
            lastOutboundReplyAtUtc,
            applicationIdsWithOpenReminder,
            data.InboxOverviewCounts);
        var selected = SortApplications(
            applications
                .Where(application => MatchesDashboardView(application, options.View, applicationIdsWithOpenReminder, lastOutboundReplyAtUtc, reviewStates))
                .Where(application => MatchesDashboardFilters(application, options))
                .ToList(),
            options,
            nextReminderByApplicationId,
            lastOutboundReplyAtUtc,
            applicationIdsWithOpenReminder,
            nowUtc);
        var (page, nextCursor) = TakePage(selected, options);
        var pageIds = page.Select(application => application.Id).ToHashSet();

        var receivedTimes = data.ApplicationEmailSummaries.ToDictionary(
            summary => summary.JobApplicationId,
            summary => summary.FirstReceivedAtUtc);
        var gmailUrls = data.ApplicationEmailSummaries
            .Where(summary => !string.IsNullOrWhiteSpace(summary.GmailUrl))
            .ToDictionary(summary => summary.JobApplicationId, summary => summary.GmailUrl);

        return new DashboardResponse(
            metrics,
            BuildInboxOverview(data, applications, resolvedUserEmail),
            BuildApplicationCards(
                page,
                reviewStates,
                nextReminderByApplicationId,
                lastOutboundReplyAtUtc,
                applicationIdsWithOpenReminder,
                receivedTimes,
                gmailUrls,
                latestEvidenceByApplication,
                nowUtc),
            BuildPendingActions(
                actionReminders.Take(6).Where(record => pageIds.Contains(record.Reminder.JobApplicationId)),
                gmailUrls,
                nowUtc),
            BuildRecentActivity(events.Take(RecentActivityLimit).Where(@event => pageIds.Contains(@event.JobApplicationId))),
            nextCursor);
    }

    private static DashboardResponse EmptyDashboard(string userEmail) =>
        new(
            new DashboardMetricsDto(0, 0, 0, 0, 0, 0, 0, 0, 0),
            new InboxOverviewDto(
                userEmail,
                0,
                0,
                null,
                ScannedCount: 0,
                TotalEstimate: 0,
                RecognizedApplications: 0,
                BackfillStatus: "idle"),
            [],
            [],
            []);

    private static List<JobApplication> SortApplications(
        List<JobApplication> applications,
        DashboardQueryOptions options,
        IReadOnlyDictionary<Guid, DashboardPendingActionRecord> nextReminderByApplicationId,
        IReadOnlyDictionary<Guid, DateTimeOffset?> lastOutboundReplyAtUtc,
        IReadOnlySet<Guid> applicationIdsWithOpenReminder,
        DateTimeOffset nowUtc)
    {
        return string.Equals(options.Sort, "urgency", StringComparison.OrdinalIgnoreCase)
            ? applications
                .OrderByDescending(application =>
                {
                    nextReminderByApplicationId.TryGetValue(application.Id, out var nextReminder);
                    return CalculateUrgencyScore(
                        application,
                        nextReminder,
                        lastOutboundReplyAtUtc.GetValueOrDefault(application.Id),
                        nowUtc);
                })
                .ThenByDescending(application => application.LastActivityAtUtc)
                .ToList()
            : applications
                .OrderByDescending(application => applicationIdsWithOpenReminder.Contains(application.Id))
                .ThenBy(application => application.Priority)
                .ThenByDescending(application => application.LastActivityAtUtc)
                .ToList();
    }

    private static (List<JobApplication> Page, string? NextCursor) TakePage(
        List<JobApplication> applications,
        DashboardQueryOptions options)
    {
        if (options.Limit is not > 0)
        {
            return (applications, null);
        }

        var offset = Math.Max(0, options.CursorOffset ?? 0);
        var page = applications.Skip(offset).Take(options.Limit.Value).ToList();
        var nextCursor = applications.Count > offset + page.Count
            ? (offset + page.Count).ToString(CultureInfo.InvariantCulture)
            : null;
        return (page, nextCursor);
    }

    private static string ResolveUserEmail(string? userEmail)
    {
        return ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
    }
}

public sealed record DashboardQueryOptions(
    string? View = null,
    int? Limit = null,
    int? CursorOffset = null,
    string? Query = null,
    string? Company = null,
    string? RoleCanonical = null,
    string? Source = null,
    string? Location = null,
    string? Sort = null)
{
    public static DashboardQueryOptions Empty { get; } = new();
}
