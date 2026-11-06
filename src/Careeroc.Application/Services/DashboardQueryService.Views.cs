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

// Which applications a view shows: review state, view and filter matching.
public sealed partial class DashboardQueryService
{
    private static ApplicationReviewState BuildApplicationReviewState(IReadOnlyList<DashboardEmailRecord> emails)
    {
        var parserConfidence = emails
            .Where(email => email.ClassificationConfidence.HasValue)
            .Select(email => email.ClassificationConfidence!.Value)
            .DefaultIfEmpty()
            .Min();
        var hasParserConfidence = emails.Any(email => email.ClassificationConfidence.HasValue);
        var requiresReview = emails.Any(email => email.RequiresManualReview);
        var reviewReason = emails
            .Where(email => email.RequiresManualReview && !string.IsNullOrWhiteSpace(email.ReviewReason))
            .OrderByDescending(email => email.ReceivedAtUtc)
            .Select(email => email.ReviewReason)
            .FirstOrDefault();

        return new ApplicationReviewState(
            hasParserConfidence ? parserConfidence : null,
            requiresReview,
            requiresReview
                ? reviewReason ?? "Linked email requires manual review."
                : null);
    }

    private static bool MatchesDashboardView(
        JobApplication application,
        string? view,
        IReadOnlySet<Guid> applicationIdsWithOpenReminder,
        IReadOnlyDictionary<Guid, DateTimeOffset?> applicationLastOutboundReplyAtUtc,
        IReadOnlyDictionary<Guid, ApplicationReviewState> applicationReviewStates)
    {
        return NormalizeView(view) switch
        {
            "needs-action" =>
                applicationIdsWithOpenReminder.Contains(application.Id) &&
                application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed,
            "needs-review" =>
                applicationReviewStates.TryGetValue(application.Id, out var state) &&
                state.RequiresReview,
            "active" => application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed,
            "waiting" =>
                application.Status is not ApplicationStatus.Rejected and not ApplicationStatus.Closed &&
                applicationLastOutboundReplyAtUtc.ContainsKey(application.Id) &&
                !applicationIdsWithOpenReminder.Contains(application.Id) &&
                !ApplicationRequiresReview(application, applicationReviewStates),
            "interviewing" => application.Status == ApplicationStatus.Interviewing,
            "offer" => application.Status == ApplicationStatus.Offer,
            "rejected" => application.Status == ApplicationStatus.Rejected,
            _ => true
        };
    }

    private static bool ApplicationRequiresReview(
        JobApplication application,
        IReadOnlyDictionary<Guid, ApplicationReviewState> applicationReviewStates)
    {
        return applicationReviewStates.TryGetValue(application.Id, out var state) &&
               state.RequiresReview;
    }

    private static bool TryBuildIdentityReviewState(
        JobApplication application,
        out ApplicationReviewState reviewState)
    {
        var reason = ResolveIdentityReviewReason(application);
        if (reason is null)
        {
            reviewState = default!;
            return false;
        }

        reviewState = new ApplicationReviewState(0.35m, true, reason);
        return true;
    }

    private static string? ResolveIdentityReviewReason(JobApplication application)
    {
        var companyLooksLikePerson = CompanyNameNormalizer.IsLikelyPersonName(application.Company);
        var companyLooksLikeClearContactName = application.Company.Contains(',', StringComparison.Ordinal);
        var roleLooksUnusable = IsSuspiciousRoleToken(application.Role);

        if ((companyLooksLikeClearContactName || companyLooksLikePerson) && roleLooksUnusable)
        {
            return "Company and role look like parser errors.";
        }

        if (companyLooksLikeClearContactName && companyLooksLikePerson)
        {
            return "Company looks like a recruiter or contact name.";
        }

        if (roleLooksUnusable)
        {
            return "Role could not be extracted confidently.";
        }

        return null;
    }

    private static bool IsSuspiciousRoleToken(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        var normalized = role.Trim().ToLowerInvariant();
        return normalized is "you" or "your" or "me" or "candidate" or "applicant";
    }

    private static ApplicationReviewState MergeApplicationReviewState(
        ApplicationReviewState? existing,
        ApplicationReviewState next)
    {
        if (existing is null)
        {
            return next;
        }

        return new ApplicationReviewState(
            MinNullable(existing.ParserConfidence, next.ParserConfidence),
            existing.RequiresReview || next.RequiresReview,
            existing.RequiresReview && !string.IsNullOrWhiteSpace(existing.ReviewReason)
                ? existing.ReviewReason
                : next.ReviewReason);
    }

    private static decimal? MinNullable(decimal? left, decimal? right)
    {
        return (left, right) switch
        {
            (null, null) => null,
            (null, _) => right,
            (_, null) => left,
            _ => Math.Min(left.Value, right.Value)
        };
    }

    private static bool MatchesDashboardFilters(JobApplication application, DashboardQueryOptions options)
    {
        if (!ContainsIfSet(application.Company, options.Company))
        {
            return false;
        }

        if (!ContainsIfSet(application.RoleCanonical ?? application.Role, options.RoleCanonical))
        {
            return false;
        }

        if (!ContainsIfSet(application.SourceSystem ?? application.Source, options.Source))
        {
            return false;
        }

        if (!ContainsIfSet(application.Location ?? string.Empty, options.Location))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.Query) &&
            !ContainsIfSet(application.Company, options.Query) &&
            !ContainsIfSet(application.Role, options.Query) &&
            !ContainsIfSet(ToLabel(application.Status), options.Query))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsIfSet(string value, string? filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
               value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeView(string? view)
    {
        return view?.Trim().ToLowerInvariant() switch
        {
            "needs-action" => "needs-action",
            "needs-review" => "needs-review",
            "active" => "active",
            "waiting" => "waiting",
            "interviewing" => "interviewing",
            "offer" => "offer",
            "rejected" => "rejected",
            "all" => "all",
            _ => "all"
        };
    }

    // Applications with mail only in inboxes the user can no longer see are hidden; applications
    // with no mail at all (added by hand) always show.
    private static HashSet<Guid> FindVisibleApplicationIds(DashboardData data)
    {
        return data.Applications
            .Where(
                application =>
                    !data.ApplicationIdsWithAnyLinkedEmail.Contains(application.Id) ||
                    data.ApplicationIdsWithVisibleLinkedEmail.Contains(application.Id))
            .Select(application => application.Id)
            .ToHashSet();
    }

    private static Dictionary<Guid, ApplicationReviewState> BuildReviewStates(
        IEnumerable<JobApplication> applications,
        IReadOnlyDictionary<Guid, List<DashboardEmailRecord>> emailsByApplication)
    {
        var reviewStates = emailsByApplication.ToDictionary(
            group => group.Key,
            group => BuildApplicationReviewState(group.Value));
        foreach (var application in applications)
        {
            if (TryBuildIdentityReviewState(application, out var identityReviewState))
            {
                reviewStates[application.Id] = MergeApplicationReviewState(
                    reviewStates.GetValueOrDefault(application.Id),
                    identityReviewState);
            }
        }

        return reviewStates;
    }

    // Reminders that count as actions: on a visible application, not a low-signal fallback, and
    // not backed by an email that is itself waiting in the review queue.
    private static List<DashboardPendingActionRecord> BuildActionReminders(
        IEnumerable<DashboardReminderRecord> reminders,
        IReadOnlySet<Guid> visibleApplicationIds,
        IReadOnlyDictionary<Guid, List<DashboardEmailRecord>> emailsByApplication)
    {
        return reminders
            .Where(
                reminder =>
                    visibleApplicationIds.Contains(reminder.JobApplicationId) &&
                    !IsLowSignalFallbackReminder(reminder))
            .Select(
                reminder =>
                {
                    emailsByApplication.TryGetValue(reminder.JobApplicationId, out var reminderEmails);
                    return new DashboardPendingActionRecord(reminder, ResolveReminderEvidence(reminder, reminderEmails ?? []));
                })
            .Where(record => record.EvidenceEmail?.RequiresManualReview != true)
            .ToList();
    }
}
