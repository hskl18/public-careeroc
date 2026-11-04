using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Analytics;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class AnalyticsQueryService
{
    public const string AllRange = "all";
    public const string Last30DaysRange = "30d";
    public const string Last90DaysRange = "90d";
    public const string AllProvider = "all";

    private static readonly HashSet<string> ReplyEventTypes =
    [
        "recruiter_outreach",
        "oa_invitation",
        "interview_invitation",
        "interview_confirmation",
        "generic_update",
        "rejection",
        "offer"
    ];

    private static readonly HashSet<string> InterviewEventTypes =
    [
        "interview_invitation",
        "interview_confirmation"
    ];

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public AnalyticsQueryService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<AnalyticsResponse> GetAnalyticsAsync(
        string? range = null,
        string? provider = null,
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedRange = NormalizeRange(range);
        var normalizedProvider = NormalizeProvider(provider);
        var resolvedUserEmail = ResolveUserEmail(userEmail);

        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.InboxAccounts)
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);

        if (user is null)
        {
            return BuildEmptyResponse();
        }

        var cutoffUtc = ResolveCutoffUtc(normalizedRange);
        var applicationsQuery = _dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == user.Id);

        if (cutoffUtc.HasValue)
        {
            applicationsQuery = applicationsQuery.Where(application => application.CreatedAtUtc >= cutoffUtc.Value);
        }

        var applications = await applicationsQuery.ToListAsync(cancellationToken);

        if (applications.Count == 0)
        {
            return BuildEmptyResponse();
        }

        var applicationIds = applications.Select(application => application.Id).ToList();
        var events = await _dbContext.JobApplicationEvents
            .AsNoTracking()
            .Where(@event => applicationIds.Contains(@event.JobApplicationId))
            .ToListAsync(cancellationToken);

        var linkedEmails = await _dbContext.InboxEmails
            .AsNoTracking()
            .Include(email => email.InboxAccount)
            .Where(email => email.JobApplicationId.HasValue && applicationIds.Contains(email.JobApplicationId.Value))
            .ToListAsync(cancellationToken);

        var visibleInboxAccountIds = user.InboxAccounts
            .Select(account => account.Id)
            .ToHashSet();
        var applicationIdsWithAnyLinkedEmail = linkedEmails
            .Select(email => email.JobApplicationId!.Value)
            .ToHashSet();
        var visibleApplicationIds = linkedEmails
            .Where(email => visibleInboxAccountIds.Contains(email.InboxAccountId))
            .Select(email => email.JobApplicationId!.Value)
            .ToHashSet();

        applications = applications
            .Where(
                application =>
                    !applicationIdsWithAnyLinkedEmail.Contains(application.Id) ||
                    visibleApplicationIds.Contains(application.Id))
            .ToList();

        if (applications.Count == 0)
        {
            return BuildEmptyResponse();
        }

        applicationIds = applications.Select(application => application.Id).ToList();
        linkedEmails = linkedEmails
            .Where(email => applicationIds.Contains(email.JobApplicationId!.Value))
            .ToList();
        events = events
            .Where(@event => applicationIds.Contains(@event.JobApplicationId))
            .ToList();

        var eventsByApplicationId = events
            .GroupBy(@event => @event.JobApplicationId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var providersByApplicationId = linkedEmails
            .GroupBy(email => email.JobApplicationId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<string>)group
                    .Select(email => email.InboxAccount.Provider)
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
        var linkedEmailsByApplicationId = linkedEmails
            .GroupBy(email => email.JobApplicationId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var snapshots = applications
            .Select(
                application =>
                {
                    var applicationEvents = GetEvents(application.Id, eventsByApplicationId);
                    var providers = providersByApplicationId.TryGetValue(application.Id, out var linkedProviders)
                        ? linkedProviders
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var applicationEmails = linkedEmailsByApplicationId.TryGetValue(application.Id, out var emails)
                        ? emails
                        : new List<InboxEmail>();

                    return CreateSnapshot(application, applicationEvents, providers, applicationEmails);
                })
            .ToList();

        if (!string.Equals(normalizedProvider, AllProvider, StringComparison.Ordinal))
        {
            snapshots = snapshots
                .Where(snapshot => snapshot.Providers.Contains(normalizedProvider))
                .ToList();
        }

        if (snapshots.Count == 0)
        {
            return BuildEmptyResponse();
        }

        var metrics = new AnalyticsMetricsDto(
            ApplicationsCount: snapshots.Count,
            UniqueCompanies: CountDistinct(snapshots.Select(snapshot => snapshot.CompanyLabel)),
            RolesCount: CountDistinct(snapshots.Select(snapshot => snapshot.Application.RoleCanonical ?? ApplicationWorkflowService.ResolveRoleCanonical(snapshot.Application.Role))),
            ReplyRate: ToPercent(snapshots.Count(snapshot => snapshot.HasReply), snapshots.Count),
            InterviewRate: ToPercent(snapshots.Count(snapshot => snapshot.HasInterview), snapshots.Count),
            OfferRate: ToPercent(snapshots.Count(snapshot => snapshot.HasOffer), snapshots.Count),
            AvgTimeToFirstResponseHours: AverageHours(snapshots.Select(snapshot => snapshot.HoursToFirstResponse)),
            ResponseRates: BuildResponseRates(snapshots));

        return new AnalyticsResponse(
            metrics,
            BuildBreakdown(snapshots, snapshot => snapshot.CompanyLabel),
            BuildBreakdown(snapshots, snapshot => snapshot.Application.RoleCanonical ?? ApplicationWorkflowService.ResolveRoleCanonical(snapshot.Application.Role)),
            BuildFunnel(snapshots),
            BuildBreakdown(snapshots, snapshot => snapshot.Application.SourceSystem ?? ResolveSourceSystem(snapshot.Application.Source, snapshot.Providers)),
            BuildBreakdown(snapshots, snapshot => snapshot.Application.Location ?? "Unknown location"),
            BuildBreakdown(snapshots, snapshot => snapshot.Application.SeniorityLevel ?? ResolveSeniority(snapshot.Application.Role)),
            BuildInsights(snapshots),
            user.InboxAccounts
                .Select(account => account.LastSyncedAtUtc)
                .DefaultIfEmpty()
                .Max());
    }

    private static AnalyticsResponse BuildEmptyResponse()
    {
        return new AnalyticsResponse(
            new AnalyticsMetricsDto(0, 0, 0, 0, 0, 0, null),
            [],
            []);
    }

    private static AnalyticsSnapshot CreateSnapshot(
        JobApplication application,
        IReadOnlyList<JobApplicationEvent> events,
        IReadOnlySet<string> providers,
        IReadOnlyList<InboxEmail> linkedEmails)
    {
        return new AnalyticsSnapshot(
            application,
            CompanyNameNormalizer.Resolve(application.Company, BuildCompanyEvidence(linkedEmails)),
            HasReply(application, events),
            HasAutoReceipt(events),
            HasHumanResponse(events),
            HasAssessment(application, events),
            HasInterview(application, events),
            HasOffer(application, events),
            HasRejection(application, events),
            GetHoursToFirstResponse(application, events),
            providers);
    }

    private static IEnumerable<string> BuildCompanyEvidence(IEnumerable<InboxEmail> linkedEmails)
    {
        foreach (var email in linkedEmails.OrderByDescending(email => email.ReceivedAtUtc))
        {
            yield return email.Subject;

            if (!string.IsNullOrWhiteSpace(email.ParsedBody))
            {
                yield return email.ParsedBody;
            }

            if (!string.IsNullOrWhiteSpace(email.RawBody))
            {
                yield return email.RawBody;
            }

            yield return email.Sender;

            if (!string.IsNullOrWhiteSpace(email.ExtractedCompany))
            {
                yield return email.ExtractedCompany;
            }
        }
    }

    private static IReadOnlyList<JobApplicationEvent> GetEvents(
        Guid applicationId,
        IReadOnlyDictionary<Guid, List<JobApplicationEvent>> eventsByApplicationId)
    {
        return eventsByApplicationId.TryGetValue(applicationId, out var events)
            ? events
            : [];
    }

    private static bool HasReply(JobApplication application, IReadOnlyList<JobApplicationEvent> events)
    {
        return application.Status != ApplicationStatus.Applied ||
               events.Any(@event => ReplyEventTypes.Contains(@event.EventType));
    }

    private static bool HasInterview(JobApplication application, IReadOnlyList<JobApplicationEvent> events)
    {
        return application.Status is ApplicationStatus.Interviewing or ApplicationStatus.Offer ||
               events.Any(@event => InterviewEventTypes.Contains(@event.EventType));
    }

    private static bool HasOffer(JobApplication application, IReadOnlyList<JobApplicationEvent> events)
    {
        return application.Status == ApplicationStatus.Offer ||
               events.Any(@event => string.Equals(@event.EventType, "offer", StringComparison.Ordinal));
    }

    private static bool HasAutoReceipt(IReadOnlyList<JobApplicationEvent> events)
    {
        return events.Any(@event => string.Equals(@event.EventType, "application_received", StringComparison.Ordinal));
    }

    private static bool HasHumanResponse(IReadOnlyList<JobApplicationEvent> events)
    {
        return events.Any(
            @event => @event.EventType is
                "recruiter_outreach" or
                "interview_invitation" or
                "interview_confirmation");
    }

    private static bool HasAssessment(JobApplication application, IReadOnlyList<JobApplicationEvent> events)
    {
        return application.Status is ApplicationStatus.OaPending or ApplicationStatus.Interviewing or ApplicationStatus.Offer ||
               events.Any(@event => string.Equals(@event.EventType, "oa_invitation", StringComparison.Ordinal));
    }

    private static bool HasRejection(JobApplication application, IReadOnlyList<JobApplicationEvent> events)
    {
        return application.Status == ApplicationStatus.Rejected ||
               events.Any(@event => string.Equals(@event.EventType, "rejection", StringComparison.Ordinal));
    }

    private static decimal? GetHoursToFirstResponse(JobApplication application, IReadOnlyList<JobApplicationEvent> events)
    {
        var firstResponse = events
            .Where(@event => ReplyEventTypes.Contains(@event.EventType))
            .Where(@event => @event.EventTimeUtc > application.CreatedAtUtc)
            .OrderBy(@event => @event.EventTimeUtc)
            .FirstOrDefault();

        if (firstResponse is null)
        {
            return null;
        }

        var elapsed = firstResponse.EventTimeUtc - application.CreatedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            return null;
        }

        return decimal.Round((decimal)elapsed.TotalHours, 1, MidpointRounding.AwayFromZero);
    }

    private static int CountDistinct(IEnumerable<string> values)
    {
        return values
            .Select(NormalizeDimension)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static string NormalizeDimension(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    public static string NormalizeRange(string? range)
    {
        return range?.Trim().ToLowerInvariant() switch
        {
            Last30DaysRange => Last30DaysRange,
            Last90DaysRange => Last90DaysRange,
            _ => AllRange
        };
    }

    public static string NormalizeProvider(string? provider)
    {
        return provider?.Trim().ToLowerInvariant() switch
        {
            ApplicationDefaults.GmailInboxProvider => ApplicationDefaults.GmailInboxProvider,
            ApplicationDefaults.DemoInboxProvider => ApplicationDefaults.DemoInboxProvider,
            _ => AllProvider
        };
    }

    private DateTimeOffset? ResolveCutoffUtc(string range)
    {
        return range switch
        {
            Last30DaysRange => _clock.UtcNow.AddDays(-30),
            Last90DaysRange => _clock.UtcNow.AddDays(-90),
            _ => null
        };
    }

    private static IReadOnlyList<AnalyticsBreakdownItemDto> BuildBreakdown(
        IEnumerable<AnalyticsSnapshot> snapshots,
        Func<AnalyticsSnapshot, string> selector)
    {
        return snapshots
            .Where(snapshot => !string.IsNullOrWhiteSpace(selector(snapshot)))
            .GroupBy(snapshot => NormalizeDimension(selector(snapshot)))
            .Select(
                group =>
                {
                    var entries = group.ToList();
                    return new AnalyticsBreakdownItemDto(
                        ResolveDisplayLabel(entries, selector),
                        entries.Count,
                        ToPercent(entries.Count(entry => entry.HasReply), entries.Count),
                        ToPercent(entries.Count(entry => entry.HasInterview), entries.Count),
                        ToPercent(entries.Count(entry => entry.HasOffer), entries.Count),
                        AverageHours(entries.Select(entry => entry.HoursToFirstResponse)),
                        BuildResponseRates(entries));
                })
            .OrderByDescending(item => item.ApplicationsCount)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveDisplayLabel(
        IEnumerable<AnalyticsSnapshot> snapshots,
        Func<AnalyticsSnapshot, string> selector)
    {
        return snapshots
            .Select(selector)
            .Select(value => value.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .First();
    }

    private static decimal? AverageHours(IEnumerable<decimal?> hours)
    {
        var values = hours
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        if (values.Count == 0)
        {
            return null;
        }

        return decimal.Round(values.Average(), 1, MidpointRounding.AwayFromZero);
    }

    private static decimal ToPercent(int count, int total)
    {
        if (count == 0 || total == 0)
        {
            return 0;
        }

        return decimal.Round(count * 100m / total, 1, MidpointRounding.AwayFromZero);
    }

    private static AnalyticsResponseRatesDto BuildResponseRates(IReadOnlyList<AnalyticsSnapshot> snapshots)
    {
        return new AnalyticsResponseRatesDto(
            ToPercent(snapshots.Count(snapshot => snapshot.HasReply), snapshots.Count),
            ToPercent(snapshots.Count(snapshot => snapshot.HasAutoReceipt), snapshots.Count),
            ToPercent(snapshots.Count(snapshot => snapshot.HasHumanResponse), snapshots.Count),
            ToPercent(snapshots.Count(snapshot => snapshot.HasAssessment), snapshots.Count),
            ToPercent(snapshots.Count(snapshot => snapshot.HasInterview), snapshots.Count),
            ToPercent(snapshots.Count(snapshot => snapshot.HasOffer), snapshots.Count),
            ToPercent(snapshots.Count(snapshot => snapshot.HasRejection), snapshots.Count));
    }

    private static IReadOnlyList<AnalyticsFunnelStageDto> BuildFunnel(IReadOnlyList<AnalyticsSnapshot> snapshots)
    {
        var applied = snapshots.Count;
        var responded = snapshots.Count(snapshot => snapshot.HasReply);
        var assessment = snapshots.Count(snapshot => snapshot.HasAssessment);
        var interview = snapshots.Count(snapshot => snapshot.HasInterview);
        var offer = snapshots.Count(snapshot => snapshot.HasOffer);

        return
        [
            new("applied", "Applied", applied, 1),
            new("responded", "Any response", responded, ToRate(responded, applied)),
            new("assessment", "Assessment", assessment, ToRate(assessment, Math.Max(responded, 1))),
            new("interview", "Interview", interview, ToRate(interview, Math.Max(assessment, 1))),
            new("offer", "Offer", offer, ToRate(offer, Math.Max(interview, 1)))
        ];
    }

    private static IReadOnlyList<AnalyticsInsightDto> BuildInsights(IReadOnlyList<AnalyticsSnapshot> snapshots)
    {
        var insights = new List<AnalyticsInsightDto>();
        var companyBreakdown = BuildBreakdown(snapshots, snapshot => snapshot.CompanyLabel);
        var wastedCompanies = companyBreakdown
            .Where(item => item.ApplicationsCount >= 5 && item.InterviewRate == 0)
            .Take(3)
            .ToList();

        if (wastedCompanies.Count > 0)
        {
            insights.Add(new AnalyticsInsightDto(
                "companies-without-interviews",
                "warning",
                $"{wastedCompanies.Count} company groups have volume but no interviews",
                string.Join(", ", wastedCompanies.Select(item => $"{item.Label} ({item.ApplicationsCount})")) +
                " have 5+ applications and no interview events.",
                "/app/applications?view=all"));
        }

        var unknownRoles = snapshots.Count(snapshot => (snapshot.Application.RoleCanonical ?? ApplicationWorkflowService.ResolveRoleCanonical(snapshot.Application.Role)) == "Other / Unknown");
        if (unknownRoles > 0)
        {
            insights.Add(new AnalyticsInsightDto(
                "unknown-role-impact",
                "warning",
                $"{unknownRoles} applications need role cleanup",
                "Unknown role labels reduce the usefulness of strategy analytics.",
                "/app/applications?view=needs-review"));
        }

        var responseRates = BuildResponseRates(snapshots);
        if (responseRates.AnyResponseRate > 0 && responseRates.AutoReceiptRate >= responseRates.HumanResponseRate)
        {
            insights.Add(new AnalyticsInsightDto(
                "auto-receipt-heavy",
                "info",
                "Most responses are not human engagement",
                $"Auto receipts are {responseRates.AutoReceiptRate:0.0}% of applications while human responses are {responseRates.HumanResponseRate:0.0}%.",
                null));
        }

        return insights;
    }

    private static decimal ToRate(int count, int total)
    {
        if (count == 0 || total == 0)
        {
            return 0;
        }

        return decimal.Round(count / (decimal)total, 3, MidpointRounding.AwayFromZero);
    }

    private static string ResolveSourceSystem(string source, IReadOnlySet<string> providers)
    {
        if (!string.IsNullOrWhiteSpace(source) && !string.Equals(source, "Email", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        return providers.Count > 0
            ? string.Join(", ", providers.Order(StringComparer.OrdinalIgnoreCase))
            : "Email";
    }

    private static string ResolveSeniority(string role)
    {
        var normalized = NormalizeDimension(role);
        if (normalized.Contains("intern"))
        {
            return "intern";
        }

        if (normalized.Contains("new grad") || normalized.Contains("newgrad") || normalized.Contains("entry"))
        {
            return "new_grad";
        }

        if (normalized.Contains("senior") || normalized.Contains("staff"))
        {
            return "senior";
        }

        return "unknown";
    }

    private static string ResolveUserEmail(string? userEmail)
    {
        return ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
    }

    private sealed record AnalyticsSnapshot(
        JobApplication Application,
        string CompanyLabel,
        bool HasReply,
        bool HasAutoReceipt,
        bool HasHumanResponse,
        bool HasAssessment,
        bool HasInterview,
        bool HasOffer,
        bool HasRejection,
        decimal? HoursToFirstResponse,
        IReadOnlySet<string> Providers);
}
