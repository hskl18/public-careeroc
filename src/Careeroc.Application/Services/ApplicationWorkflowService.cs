using System.Text.Json;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class ApplicationWorkflowService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public ApplicationWorkflowService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<ApplicationMatchResult> MatchOrCreateApplicationAsync(
        Guid userId,
        Guid? currentApplicationId,
        string company,
        string role,
        ApplicationStatus recommendedStatus,
        EmailCategory category,
        DateTimeOffset activityAtUtc,
        bool allowAutoCreate,
        CancellationToken cancellationToken = default)
    {
        if (currentApplicationId.HasValue)
        {
            var current = await _dbContext.JobApplications
                .FirstAsync(application => application.Id == currentApplicationId.Value, cancellationToken);

            return new ApplicationMatchResult(current, false, 1.00m, null);
        }

        if (!CompanyNameNormalizer.IsUsableCompanyName(company))
        {
            return new ApplicationMatchResult(
                null,
                false,
                0.35m,
                "Company could not be extracted confidently.");
        }

        var normalizedCompany = NormalizeCompanyIdentity(company);
        var normalizedRole = Normalize(role);

        var userApplications = await _dbContext.JobApplications
            .Where(
                application =>
                    application.UserId == userId)
            .OrderByDescending(application => application.LastActivityAtUtc)
            .ToListAsync(cancellationToken);
        var sameCompanyApplications = userApplications
            .Where(application => NormalizeCompanyIdentity(application.Company) == normalizedCompany)
            .ToList();

        var existing = sameCompanyApplications
            .FirstOrDefault(application => Normalize(application.Role) == normalizedRole);

        if (existing is not null)
        {
            return new ApplicationMatchResult(existing, false, 0.95m, null);
        }

        var companyOnlyMatch = FindCompanyOnlyMatch(sameCompanyApplications, role);
        if (companyOnlyMatch.Application is not null)
        {
            BackfillApplicationIdentity(companyOnlyMatch.Application, company, role);
            return companyOnlyMatch;
        }

        if (!allowAutoCreate)
        {
            return new ApplicationMatchResult(
                null,
                false,
                companyOnlyMatch.MatchingConfidence > 0
                    ? companyOnlyMatch.MatchingConfidence
                    : 0.35m,
                companyOnlyMatch.ReviewReason ?? "Could not confidently map this email to an existing application.");
        }

        var created = new JobApplication
        {
            UserId = userId,
            Company = NormalizeForStorage(company, ApplicationDefaults.UnknownCompany),
            Role = NormalizeForStorage(role, ApplicationDefaults.UnknownRole),
            RoleCanonical = ResolveRoleCanonical(role),
            Source = "Email",
            SourceSystem = "gmail",
            SeniorityLevel = ResolveSeniority(role),
            Status = recommendedStatus,
            Priority = ResolvePriority(category),
            LastActivityAtUtc = activityAtUtc,
            CreatedAtUtc = activityAtUtc
        };

        _dbContext.JobApplications.Add(created);
        return new ApplicationMatchResult(created, true, 0.82m, null);
    }

    public ApplicationStatus ApplyStatusAndPriority(
        JobApplication application,
        EmailCategory category,
        ApplicationStatus recommendedStatus,
        DateTimeOffset activityAtUtc)
    {
        var resolvedStatus = ResolveStatus(application, recommendedStatus, category, activityAtUtc);
        application.Status = resolvedStatus;
        application.Priority = ResolvePriority(category);
        application.LastActivityAtUtc = activityAtUtc;
        return resolvedStatus;
    }

    // A rejection or closure ends every open task; an offer ends the interview and assessment steps.
    public async Task<int> DismissStaleRemindersAsync(
        JobApplication application,
        CancellationToken cancellationToken = default)
    {
        var terminal = IsTerminalStatus(application.Status);
        if (!terminal && application.Status != ApplicationStatus.Offer)
        {
            return 0;
        }

        var openReminders = await _dbContext.Reminders
            .Where(
                reminder =>
                    reminder.JobApplicationId == application.Id &&
                    reminder.Status == ReminderStatus.Open &&
                    (terminal ||
                     reminder.Type == ReminderType.InterviewPreparation ||
                     reminder.Type == ReminderType.AssessmentDeadline))
            .ToListAsync(cancellationToken);

        foreach (var reminder in openReminders)
        {
            reminder.Status = ReminderStatus.Dismissed;
        }

        if (openReminders.Count > 0)
        {
            _dbContext.JobApplicationEvents.Add(new JobApplicationEvent
            {
                JobApplicationId = application.Id,
                EventType = "reminder_dismissed",
                EventTimeUtc = _clock.UtcNow,
                ExtractedDataJson = JsonSerializer.Serialize(
                    new
                    {
                        reason = terminal ? "terminal_application_status" : "offer_received",
                        application.Status,
                        dismissedReminders = openReminders.Count
                    })
            });
        }

        return openReminders.Count;
    }

    public async Task<bool> EnsureReminderAsync(
        JobApplication application,
        FollowUpTaskPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!plan.ShouldCreate)
        {
            return false;
        }

        var normalizedDueAtUtc = plan.DueAtUtc.NormalizeUtc();
        var existing = await _dbContext.Reminders
            .Where(
                reminder =>
                    reminder.JobApplicationId == application.Id &&
                    reminder.Type == plan.Type &&
                    reminder.Status == ReminderStatus.Open)
            .OrderBy(reminder => reminder.DueAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (ShouldRefreshReminder(existing, plan, normalizedDueAtUtc))
            {
                existing.Title = NormalizeReminderTitle(plan.Title);
                existing.DueAtUtc = normalizedDueAtUtc;
            }

            return false;
        }

        _dbContext.Reminders.Add(new Reminder
        {
            JobApplicationId = application.Id,
            Type = plan.Type,
            Title = NormalizeReminderTitle(plan.Title),
            DueAtUtc = normalizedDueAtUtc
        });

        return true;
    }

    public async Task<bool> EnsureReminderAsync(
        JobApplication application,
        EmailCategory category,
        DateTimeOffset? dueDateUtc,
        CancellationToken cancellationToken = default)
    {
        if (!IsActionableReminderCategory(category))
        {
            return false;
        }

        var plan = new FollowUpTaskPlan(
            true,
            category switch
            {
                EmailCategory.OaInvitation => ReminderType.AssessmentDeadline,
                EmailCategory.InterviewInvitation or EmailCategory.InterviewConfirmation => ReminderType.InterviewPreparation,
                _ => ReminderType.FollowUp
            },
            BuildReminderTitle(application, category),
            dueDateUtc.NormalizeUtc() ?? _clock.UtcNow.AddDays(2),
            $"Planned from {category} workflow state.",
            "Application Workflow Service");

        return await EnsureReminderAsync(application, plan, cancellationToken);
    }

    public bool RefreshReminderFromExplicitEvidence(
        Reminder reminder,
        DateTimeOffset explicitDueAtUtc,
        string? title = null)
    {
        var normalizedDueAtUtc = explicitDueAtUtc.NormalizeUtc();
        var changed = false;

        if (reminder.DueAtUtc != normalizedDueAtUtc)
        {
            reminder.DueAtUtc = normalizedDueAtUtc;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            var normalizedTitle = NormalizeReminderTitle(title);
            if (!string.Equals(reminder.Title, normalizedTitle, StringComparison.Ordinal))
            {
                reminder.Title = normalizedTitle;
                changed = true;
            }
        }

        return changed;
    }

    public async Task UpsertEmailEventAsync(
        JobApplication application,
        InboxEmail inboxEmail,
        string eventType,
        DateTimeOffset eventTimeUtc,
        object extractedData,
        CancellationToken cancellationToken = default)
    {
        var existingEvent = await _dbContext.JobApplicationEvents
            .FirstOrDefaultAsync(@event => @event.InboxEmailId == inboxEmail.Id, cancellationToken);

        var extractedDataJson = JsonSerializer.Serialize(extractedData);

        if (existingEvent is null)
        {
            _dbContext.JobApplicationEvents.Add(new JobApplicationEvent
            {
                JobApplicationId = application.Id,
                InboxEmailId = inboxEmail.Id,
                EventType = eventType,
                EventTimeUtc = eventTimeUtc,
                ExtractedDataJson = extractedDataJson
            });

            return;
        }

        existingEvent.JobApplicationId = application.Id;
        existingEvent.EventType = eventType;
        existingEvent.EventTimeUtc = eventTimeUtc;
        existingEvent.ExtractedDataJson = extractedDataJson;
    }

    public static ApplicationStatus ToRecommendedStatus(EmailCategory category)
    {
        return category switch
        {
            EmailCategory.ApplicationReceived => ApplicationStatus.Applied,
            EmailCategory.RecruiterOutreach => ApplicationStatus.RecruiterContact,
            EmailCategory.OaInvitation => ApplicationStatus.OaPending,
            EmailCategory.InterviewInvitation => ApplicationStatus.Interviewing,
            EmailCategory.InterviewConfirmation => ApplicationStatus.Interviewing,
            EmailCategory.Rejection => ApplicationStatus.Rejected,
            EmailCategory.Offer => ApplicationStatus.Offer,
            _ => ApplicationStatus.Applied
        };
    }

    public static bool CanCreateApplication(string? company, string? role)
    {
        return CompanyNameNormalizer.IsUsableCompanyName(company);
    }

    public static string ToApiLabel(string value)
    {
        return string.Concat(
            value.Select(
                (character, index) =>
                    index > 0 && char.IsUpper(character)
                        ? $"_{char.ToLowerInvariant(character)}"
                        : char.ToLowerInvariant(character).ToString()));
    }

    public static bool IsTerminalStatus(ApplicationStatus status)
    {
        return status is ApplicationStatus.Rejected or ApplicationStatus.Closed;
    }

    public static bool IsActiveWorkflowCategory(EmailCategory category)
    {
        return category is
            EmailCategory.RecruiterOutreach or
            EmailCategory.OaInvitation or
            EmailCategory.InterviewInvitation or
            EmailCategory.InterviewConfirmation or
            EmailCategory.Offer;
    }

    private static ApplicationStatus ResolveStatus(
        JobApplication application,
        ApplicationStatus recommendedStatus,
        EmailCategory category,
        DateTimeOffset activityAtUtc)
    {
        if (recommendedStatus is ApplicationStatus.Rejected or ApplicationStatus.Offer or ApplicationStatus.Closed)
        {
            return recommendedStatus;
        }

        if (category == EmailCategory.GenericUpdate)
        {
            return application.Status;
        }

        if (IsTerminalStatus(application.Status))
        {
            return IsActiveWorkflowCategory(category) && activityAtUtc >= application.LastActivityAtUtc
                ? recommendedStatus
                : application.Status;
        }

        return GetStatusRank(recommendedStatus) >= GetStatusRank(application.Status)
            ? recommendedStatus
            : application.Status;
    }

    private static int GetStatusRank(ApplicationStatus status)
    {
        return status switch
        {
            ApplicationStatus.Applied => 10,
            ApplicationStatus.RecruiterContact => 20,
            ApplicationStatus.OaPending => 30,
            ApplicationStatus.Interviewing => 40,
            ApplicationStatus.Offer => 50,
            ApplicationStatus.Rejected => 60,
            ApplicationStatus.Closed => 70,
            _ => 0
        };
    }

    private static int ResolvePriority(EmailCategory category)
    {
        return category switch
        {
            EmailCategory.OaInvitation => 5,
            EmailCategory.InterviewInvitation => 5,
            EmailCategory.InterviewConfirmation => 5,
            EmailCategory.Offer => 5,
            EmailCategory.RecruiterOutreach => 4,
            EmailCategory.ApplicationReceived => 3,
            EmailCategory.GenericUpdate => 3,
            EmailCategory.Rejection => 1,
            _ => 3
        };
    }

    private static string BuildReminderTitle(JobApplication application, EmailCategory category)
    {
        return category switch
        {
            EmailCategory.OaInvitation => $"Complete assessment for {application.Company}",
            EmailCategory.InterviewInvitation => $"Prepare for interview with {application.Company}",
            EmailCategory.InterviewConfirmation => $"Confirm interview details for {application.Company}",
            EmailCategory.RecruiterOutreach => $"Reply to recruiter at {application.Company}",
            EmailCategory.Offer => $"Review the offer from {application.Company}",
            _ => $"Review next step for {application.Company}"
        };
    }

    private bool ShouldRefreshReminder(
        Reminder existing,
        FollowUpTaskPlan plan,
        DateTimeOffset normalizedDueAtUtc)
    {
        if (IsLowSignalFallbackReminder(existing))
        {
            return true;
        }

        if (normalizedDueAtUtc < existing.DueAtUtc)
        {
            return true;
        }

        var stalePastCutoffUtc = _clock.UtcNow.AddDays(-7);
        if (existing.DueAtUtc < stalePastCutoffUtc && normalizedDueAtUtc >= stalePastCutoffUtc)
        {
            return true;
        }

        return false;
    }

    private static string NormalizeReminderTitle(string title)
    {
        return string.IsNullOrWhiteSpace(title)
            ? "Review next recruiting step"
            : title.Trim();
    }

    public static bool IsLowSignalFallbackReminder(Reminder reminder)
    {
        return reminder.Type == ReminderType.FollowUp &&
               reminder.Title.TrimStart().StartsWith(
                   "Review next step for ",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActionableReminderCategory(EmailCategory category)
    {
        return category is
            EmailCategory.RecruiterOutreach or
            EmailCategory.OaInvitation or
            EmailCategory.InterviewInvitation or
            EmailCategory.InterviewConfirmation or
            EmailCategory.Offer;
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    // Within one email thread, "Lumenrobotics" from a domain and "Lumen Robotics" from a display name are the same employer.
    public static bool IsSameCompanyIdentity(string existing, string candidate)
    {
        var existingIdentity = NormalizeCompanyIdentity(existing).Replace(" ", string.Empty, StringComparison.Ordinal);
        var candidateIdentity = NormalizeCompanyIdentity(candidate).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (existingIdentity.Length < 3 || candidateIdentity.Length < 3)
        {
            return string.Equals(existingIdentity, candidateIdentity, StringComparison.Ordinal);
        }

        return existingIdentity.StartsWith(candidateIdentity, StringComparison.Ordinal) ||
               candidateIdentity.StartsWith(existingIdentity, StringComparison.Ordinal);
    }

    private static string NormalizeCompanyIdentity(string value)
    {
        var normalizedCharacters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();

        var normalized = new string(normalizedCharacters);
        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token is not (
                "inc" or
                "incorporated" or
                "llc" or
                "ltd" or
                "limited" or
                "co" or
                "company" or
                "corp" or
                "corporation" or
                "plc" or
                "gmbh" or
                "ag" or
                "lp" or
                "llp"));

        return string.Concat(tokens);
    }

    private static ApplicationMatchResult FindCompanyOnlyMatch(
        IReadOnlyList<JobApplication> sameCompanyApplications,
        string parsedRole)
    {
        if (sameCompanyApplications.Count == 0)
        {
            return new ApplicationMatchResult(null, false, 0m, null);
        }

        var unknownRoleCandidates = sameCompanyApplications
            .Where(application => IsUnknownRole(application.Role))
            .ToList();

        if (!IsUnknownRole(parsedRole) && unknownRoleCandidates.Count == 1)
        {
            return new ApplicationMatchResult(
                unknownRoleCandidates[0],
                false,
                0.78m,
                "Matched by company because the existing application was created before the role was known.");
        }

        if (sameCompanyApplications.Count == 1)
        {
            var onlyApplication = sameCompanyApplications[0];
            if (!RolesAreCompatible(onlyApplication.Role, parsedRole))
            {
                return new ApplicationMatchResult(
                    null,
                    false,
                    0.42m,
                    "Only one application exists for this company, but the parsed role differs from the existing role.");
            }

            return new ApplicationMatchResult(
                onlyApplication,
                false,
                0.72m,
                "Matched by company because only one application exists for this company.");
        }

        if (IsUnknownRole(parsedRole))
        {
            return new ApplicationMatchResult(
                null,
                false,
                0.48m,
                "Multiple applications exist for this company, and the role was not extracted confidently.");
        }

        return new ApplicationMatchResult(null, false, 0.35m, null);
    }

    private static void BackfillApplicationIdentity(
        JobApplication application,
        string parsedCompany,
        string parsedRole)
    {
        if (string.Equals(application.Company, ApplicationDefaults.UnknownCompany, StringComparison.OrdinalIgnoreCase) &&
            CompanyNameNormalizer.IsUsableCompanyName(parsedCompany))
        {
            application.Company = parsedCompany.Trim();
        }

        if (IsUnknownRole(application.Role) && !IsUnknownRole(parsedRole))
        {
            application.Role = parsedRole.Trim();
        }

        application.RoleCanonical = ResolveRoleCanonical(application.Role);
        application.SeniorityLevel = ResolveSeniority(application.Role);
    }

    private static bool IsUnknownRole(string? role)
    {
        return string.IsNullOrWhiteSpace(role) ||
               string.Equals(role.Trim(), ApplicationDefaults.UnknownRole, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RolesAreCompatible(string existingRole, string parsedRole)
    {
        if (IsUnknownRole(existingRole) || IsUnknownRole(parsedRole))
        {
            return true;
        }

        var existing = NormalizeRoleIdentity(existingRole);
        var parsed = NormalizeRoleIdentity(parsedRole);

        return existing == parsed ||
               existing.Contains(parsed, StringComparison.Ordinal) ||
               parsed.Contains(existing, StringComparison.Ordinal);
    }

    private static string NormalizeRoleIdentity(string role)
    {
        var normalizedCharacters = role
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();

        var normalized = new string(normalizedCharacters);
        return string.Join(
            ' ',
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => token is not ("role" or "position" or "opening" or "opportunity")));
    }

    private static string NormalizeForStorage(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    internal static string ResolveRoleCanonical(string role)
    {
        var normalized = NormalizeRoleIdentity(role);
        // Short tokens match whole words only: "ai" must not match "maintenance" or "email".
        var padded = $" {normalized} ";
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "unknown", StringComparison.Ordinal) ||
            string.Equals(normalized, "unknown role", StringComparison.Ordinal))
        {
            return "Other / Unknown";
        }

        if (normalized.Contains("frontend", StringComparison.Ordinal) || normalized.Contains("front end", StringComparison.Ordinal))
        {
            return "Software Engineering / Frontend";
        }

        if (normalized.Contains("backend", StringComparison.Ordinal) || normalized.Contains("back end", StringComparison.Ordinal))
        {
            return "Software Engineering / Backend";
        }

        if (normalized.Contains("machine learning", StringComparison.Ordinal) ||
            padded.Contains(" ml ", StringComparison.Ordinal) ||
            padded.Contains(" ai ", StringComparison.Ordinal))
        {
            return "ML / AI";
        }

        if (normalized.Contains("data", StringComparison.Ordinal) || normalized.Contains("analytics", StringComparison.Ordinal))
        {
            return "Data / Analytics";
        }

        if (normalized.Contains("research", StringComparison.Ordinal))
        {
            return "Research";
        }

        if (normalized.Contains("devops", StringComparison.Ordinal) ||
            normalized.Contains("cloud", StringComparison.Ordinal) ||
            normalized.Contains("infra", StringComparison.Ordinal))
        {
            return "DevOps / Cloud / Infra";
        }

        if (normalized.Contains("software", StringComparison.Ordinal) ||
            normalized.Contains("engineer", StringComparison.Ordinal) ||
            padded.Contains(" swe ", StringComparison.Ordinal))
        {
            return "Software Engineering";
        }

        if (normalized.Contains("product", StringComparison.Ordinal) || normalized.Contains("business", StringComparison.Ordinal))
        {
            return "Product / Business";
        }

        return "Other / Unknown";
    }

    private static string ResolveSeniority(string role)
    {
        var normalized = NormalizeRoleIdentity(role);
        if (normalized.Contains("intern", StringComparison.Ordinal))
        {
            return "intern";
        }

        if (normalized.Contains("new grad", StringComparison.Ordinal) ||
            normalized.Contains("newgrad", StringComparison.Ordinal) ||
            normalized.Contains("entry", StringComparison.Ordinal))
        {
            return "new_grad";
        }

        if (normalized.Contains("senior", StringComparison.Ordinal) ||
            normalized.Contains("staff", StringComparison.Ordinal))
        {
            return "senior";
        }

        return "unknown";
    }
}
