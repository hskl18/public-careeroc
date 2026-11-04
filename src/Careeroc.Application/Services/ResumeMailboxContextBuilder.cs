using System.Text.RegularExpressions;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class ResumeMailboxContextBuilder
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public ResumeMailboxContextBuilder(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<ResumeMailboxContextSnapshot> BuildForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var applications = await _dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == userId)
            .OrderByDescending(application => application.LastActivityAtUtc)
            .ToListAsync(cancellationToken);
        var resumeVersions = await _dbContext.ResumeVersions
            .AsNoTracking()
            .Where(version => version.ResumeDocument.UserId == userId)
            .Include(version => version.ResumeDocument)
            .Include(version => version.Evaluation)
            .OrderBy(version => version.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var confirmedMemoryFacts = await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .Where(entry =>
                entry.UserId == userId &&
                entry.ConfirmationStatus == "confirmed" &&
                // A previous review is the orchestrator's own output, not a fact about the user.
                entry.Kind != "orchestrator" &&
                (entry.ExpiresAtUtc == null || entry.ExpiresAtUtc > _clock.UtcNow))
            .OrderByDescending(entry => entry.UpdatedAtUtc)
            .Select(entry => string.IsNullOrWhiteSpace(entry.Summary) ? entry.Value : entry.Summary!)
            .Take(6)
            .ToListAsync(cancellationToken);

        return new ResumeMailboxContextSnapshot(
            applications,
            resumeVersions,
            confirmedMemoryFacts,
            Build(applications, resumeVersions, confirmedMemoryFacts));
    }

    public ResumeMailboxContext Build(
        IReadOnlyList<JobApplication> applications,
        IReadOnlyList<ResumeVersion> resumeVersions,
        IReadOnlyList<string> confirmedMemoryFacts)
    {
        var topTargetRoles = applications
            .Select(application => NormalizeRoleFamily(application.RoleCanonical ?? application.Role))
            .Where(role => role.Length > 0)
            .GroupBy(role => role, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key} ({group.Count()} applications)")
            .Take(5)
            .ToList();
        var positiveOutcomeApplications = applications
            .Where(application => application.Status is ApplicationStatus.Interviewing or ApplicationStatus.Offer)
            .OrderByDescending(application => application.LastActivityAtUtc)
            .ToList();
        var companiesWithPositiveOutcomes = positiveOutcomeApplications
            .Select(application => $"{application.Company} - {application.Role} ({FormatApplicationStatus(application.Status)})")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        var repeatedRejectedRoleFamilies = applications
            .Where(application => application.Status == ApplicationStatus.Rejected)
            .Select(application => NormalizeRoleFamily(application.RoleCanonical ?? application.Role))
            .Where(role => role.Length > 0)
            .GroupBy(role => role, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key} ({group.Count()} rejections)")
            .Take(5)
            .ToList();
        var latestActionSignals = applications
            .Where(application => application.Status is ApplicationStatus.OaPending or ApplicationStatus.Interviewing or ApplicationStatus.Offer or ApplicationStatus.RecruiterContact)
            .OrderByDescending(application => application.LastActivityAtUtc)
            .Select(application => $"{application.Company} - {application.Role}: {FormatApplicationStatus(application.Status)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        return new ResumeMailboxContext(
            topTargetRoles,
            companiesWithPositiveOutcomes,
            repeatedRejectedRoleFamilies,
            latestActionSignals,
            FindResumeVersionLikelyUsedBeforePositiveOutcomes(resumeVersions, positiveOutcomeApplications),
            confirmedMemoryFacts
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList(),
            BuildEvidenceLinks(applications));
    }

    public static Guid? FindResumeVersionLikelyUsedBeforePositiveOutcomes(
        IReadOnlyList<ResumeVersion> resumeVersions,
        IReadOnlyList<JobApplication> positiveOutcomeApplications)
    {
        if (resumeVersions.Count == 0 || positiveOutcomeApplications.Count == 0)
        {
            return null;
        }

        var candidateCounts = new Dictionary<Guid, int>();
        foreach (var outcome in positiveOutcomeApplications)
        {
            var candidate = resumeVersions
                .LastOrDefault(version => version.CreatedAtUtc <= outcome.LastActivityAtUtc) ??
                            resumeVersions.First();
            candidateCounts[candidate.Id] = candidateCounts.GetValueOrDefault(candidate.Id) + 1;
        }

        return candidateCounts
            .OrderByDescending(pair => pair.Value)
            .ThenByDescending(pair => resumeVersions.First(version => version.Id == pair.Key).CreatedAtUtc)
            .Select(pair => (Guid?)pair.Key)
            .FirstOrDefault();
    }

    private static string NormalizeRoleFamily(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value.Trim(), @"\s+", " ");
    }

    internal static string FormatApplicationStatus(ApplicationStatus status)
    {
        return status switch
        {
            ApplicationStatus.OaPending => "assessment pending",
            ApplicationStatus.RecruiterContact => "recruiter contact",
            ApplicationStatus.Interviewing => "interview",
            ApplicationStatus.Offer => "offer",
            ApplicationStatus.Rejected => "rejected",
            ApplicationStatus.Closed => "closed",
            _ => "applied"
        };
    }

    private static IReadOnlyList<ResumeMailboxEvidenceLink> BuildEvidenceLinks(
        IReadOnlyList<JobApplication> applications)
    {
        return applications
            .Where(application =>
                application.Status is ApplicationStatus.Interviewing or
                    ApplicationStatus.Offer or
                    ApplicationStatus.Rejected or
                    ApplicationStatus.OaPending)
            .OrderByDescending(application => application.LastActivityAtUtc)
            .Take(12)
            .Select(
                application => new ResumeMailboxEvidenceLink(
                    "job_application",
                    application.Id,
                    $"{application.Company} - {application.Role}: {FormatApplicationStatus(application.Status)}",
                    application.LastActivityAtUtc))
            .ToList();
    }
}

public sealed record ResumeMailboxContextSnapshot(
    IReadOnlyList<JobApplication> Applications,
    IReadOnlyList<ResumeVersion> ResumeVersions,
    IReadOnlyList<string> ConfirmedMemoryFacts,
    ResumeMailboxContext Context);
