using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Careeroc.Application.Services;

public sealed class ReminderEvidenceRefreshService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IExplicitDueDateExtractor _explicitDueDateExtractor;
    private readonly ApplicationWorkflowService _applicationWorkflowService;
    private readonly IClock _clock;
    private readonly ILogger<ReminderEvidenceRefreshService> _logger;

    public ReminderEvidenceRefreshService(
        ICareerocDbContext dbContext,
        IExplicitDueDateExtractor explicitDueDateExtractor,
        ApplicationWorkflowService applicationWorkflowService,
        IClock clock)
        : this(
            dbContext,
            explicitDueDateExtractor,
            applicationWorkflowService,
            clock,
            NullLogger<ReminderEvidenceRefreshService>.Instance)
    {
    }

    public ReminderEvidenceRefreshService(
        ICareerocDbContext dbContext,
        IExplicitDueDateExtractor explicitDueDateExtractor,
        ApplicationWorkflowService applicationWorkflowService,
        IClock clock,
        ILogger<ReminderEvidenceRefreshService> logger)
    {
        _dbContext = dbContext;
        _explicitDueDateExtractor = explicitDueDateExtractor;
        _applicationWorkflowService = applicationWorkflowService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ReminderEvidenceRefreshResult> RefreshPendingRemindersAsync(
        CancellationToken cancellationToken = default)
    {
        return await RefreshPendingRemindersAsync(null, cancellationToken);
    }

    public async Task<ReminderEvidenceRefreshResult> RefreshPendingRemindersAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var refreshStartedAtUtc = _clock.UtcNow;
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        var query = _dbContext.Reminders
            .Include(reminder => reminder.JobApplication)
            .Where(
                reminder =>
                    reminder.Status == ReminderStatus.Open &&
                    reminder.JobApplication.Status != ApplicationStatus.Rejected &&
                    reminder.JobApplication.Status != ApplicationStatus.Closed &&
                    (reminder.EvidenceRefreshedAtUtc == null ||
                     _dbContext.InboxEmails.Any(
                         email =>
                             email.JobApplicationId == reminder.JobApplicationId &&
                             (email.ProcessedAtUtc ?? email.ReceivedAtUtc) > reminder.EvidenceRefreshedAtUtc)));

        if (resolvedUserEmail is not null)
        {
            query = query.Where(reminder => reminder.JobApplication.User.Email == resolvedUserEmail);
        }

        var reminders = await query
            .OrderBy(reminder => reminder.DueAtUtc)
            .ToListAsync(cancellationToken);

        return await RefreshRemindersAsync(reminders, refreshStartedAtUtc, cancellationToken);
    }

    public async Task<ReminderEvidenceRefreshResult?> RefreshReminderAsync(
        Guid reminderId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        if (resolvedUserEmail is null)
        {
            return null;
        }

        var refreshStartedAtUtc = _clock.UtcNow;
        var reminder = await _dbContext.Reminders
            .Include(candidate => candidate.JobApplication)
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.Id == reminderId &&
                    candidate.Status == ReminderStatus.Open &&
                    candidate.JobApplication.User.Email == resolvedUserEmail &&
                    candidate.JobApplication.Status != ApplicationStatus.Rejected &&
                    candidate.JobApplication.Status != ApplicationStatus.Closed,
                cancellationToken);

        return reminder is null
            ? null
            : await RefreshRemindersAsync([reminder], refreshStartedAtUtc, cancellationToken);
    }

    private async Task<ReminderEvidenceRefreshResult> RefreshRemindersAsync(
        IReadOnlyList<Reminder> reminders,
        DateTimeOffset refreshStartedAtUtc,
        CancellationToken cancellationToken)
    {
        if (reminders.Count == 0)
        {
            return new ReminderEvidenceRefreshResult(0, 0);
        }

        var applicationIds = reminders
            .Select(reminder => reminder.JobApplicationId)
            .Distinct()
            .ToList();
        var emails = await _dbContext.InboxEmails
            .Include(email => email.InboxAccount)
            .ThenInclude(account => account.User)
            .Where(
                email =>
                    email.JobApplicationId.HasValue &&
                    applicationIds.Contains(email.JobApplicationId.Value))
            .OrderByDescending(email => email.ReceivedAtUtc)
            .ToListAsync(cancellationToken);
        var emailsByApplicationId = emails
            .Where(email => email.JobApplicationId.HasValue)
            .GroupBy(email => email.JobApplicationId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<InboxEmail>)group.ToList());

        _logger.LogInformation(
            "Reminder evidence refresh scanning {ReminderCount} reminders across {ApplicationCount} applications with {LinkedEmailCount} linked emails.",
            reminders.Count,
            applicationIds.Count,
            emails.Count);

        var remindersRefreshed = 0;
        var emailsUpdated = 0;

        foreach (var reminder in reminders)
        {
            if (!emailsByApplicationId.TryGetValue(reminder.JobApplicationId, out var applicationEmails))
            {
                _logger.LogInformation(
                    "Reminder evidence refresh found no linked emails for reminder {ReminderId} ({Title}) on application {ApplicationId} ({Company} / {Role}).",
                    reminder.Id,
                    reminder.Title,
                    reminder.JobApplicationId,
                    reminder.JobApplication.Company,
                    reminder.JobApplication.Role);
                reminder.EvidenceRefreshedAtUtc = refreshStartedAtUtc;
                continue;
            }

            var evidence = await ResolveReminderEvidenceAsync(
                reminder,
                applicationEmails,
                cancellationToken);
            emailsUpdated += evidence.EmailsUpdated;
            reminder.EvidenceRefreshedAtUtc = refreshStartedAtUtc;

            if (evidence.Email is null || !evidence.DueDateUtc.HasValue)
            {
                _logger.LogInformation(
                    "Reminder evidence refresh found no deadline for reminder {ReminderId} ({Title}) on application {ApplicationId} after scanning {ScannedCandidateCount} candidate emails from {LinkedEmailCount} linked emails.",
                    reminder.Id,
                    reminder.Title,
                    reminder.JobApplicationId,
                    evidence.ScannedCandidateCount,
                    applicationEmails.Count);
                continue;
            }

            var reminderUpdated = _applicationWorkflowService.RefreshReminderFromExplicitEvidence(reminder, evidence.DueDateUtc.Value);
            _logger.LogInformation(
                "Reminder evidence refresh selected email {EmailId} (subject: {Subject}) with due date {DueDateUtc} for reminder {ReminderId} ({Title}); reminder updated: {ReminderUpdated}.",
                evidence.Email.Id,
                SubjectForLog(evidence.Email.Subject),
                evidence.DueDateUtc.Value,
                reminder.Id,
                reminder.Title,
                reminderUpdated);

            if (reminderUpdated)
            {
                remindersRefreshed++;
            }
        }

        if (reminders.Count > 0 || remindersRefreshed > 0 || emailsUpdated > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ReminderEvidenceRefreshResult(remindersRefreshed, emailsUpdated);
    }

    private static string? ResolveUserEmail(string? userEmail)
    {
        return string.IsNullOrWhiteSpace(userEmail)
            ? null
            : userEmail.Trim().ToLowerInvariant();
    }

    private async Task<ReminderEvidenceSelection> ResolveReminderEvidenceAsync(
        Reminder reminder,
        IReadOnlyList<InboxEmail> emails,
        CancellationToken cancellationToken)
    {
        if (emails.Count == 0)
        {
            return new ReminderEvidenceSelection(null, null, 0, 0, 0);
        }

        EmailCategory[] preferredCategories = reminder.Type switch
        {
            ReminderType.AssessmentDeadline => [EmailCategory.OaInvitation],
            ReminderType.InterviewPreparation => [EmailCategory.InterviewConfirmation, EmailCategory.InterviewInvitation],
            ReminderType.FollowUp => [EmailCategory.RecruiterOutreach],
            _ => []
        };

        var actionableCandidates = emails
            .Where(email => email.ActionRequired && !email.RequiresManualReview)
            .ToList();
        var categoryCandidates = actionableCandidates
            .Where(email => preferredCategories.Length == 0 || (email.Category.HasValue && preferredCategories.Contains(email.Category.Value)))
            .ToList();
        var fallbackCandidates = emails
            .Where(email => !email.RequiresManualReview)
            .ToList();

        var candidateEmails = (categoryCandidates.Count > 0
                ? categoryCandidates
                : actionableCandidates.Count > 0
                    ? actionableCandidates
                    : fallbackCandidates)
            .OrderByDescending(email => email.ReceivedAtUtc)
            .ToList();

        var candidates = new List<ReminderEvidenceCandidate>();
        var emailsUpdated = 0;

        foreach (var email in candidateEmails)
        {
            var storedDueDateUtc = email.DueDateUtc.NormalizeUtc();
            var dueDateUtc = storedDueDateUtc;
            var dueDateSource = storedDueDateUtc.HasValue ? "stored" : "none";
            if (!dueDateUtc.HasValue)
            {
                dueDateUtc = (await _explicitDueDateExtractor.ExtractDueDateUtcAsync(
                    email,
                    cancellationToken)).NormalizeUtc();

                if (dueDateUtc.HasValue)
                {
                    email.DueDateUtc = dueDateUtc;
                    emailsUpdated++;
                    dueDateSource = "extracted";
                }
            }

            _logger.LogInformation(
                "Reminder evidence candidate for reminder {ReminderId}: email {EmailId}, subject: {Subject}, category: {Category}, actionRequired: {ActionRequired}, manualReview: {RequiresManualReview}, storedDueDateUtc: {StoredDueDateUtc}, resolvedDueDateUtc: {ResolvedDueDateUtc}, dueDateSource: {DueDateSource}.",
                reminder.Id,
                email.Id,
                SubjectForLog(email.Subject),
                email.Category,
                email.ActionRequired,
                email.RequiresManualReview,
                storedDueDateUtc,
                dueDateUtc,
                dueDateSource);

            if (dueDateUtc.HasValue)
            {
                candidates.Add(
                    new ReminderEvidenceCandidate(
                        email,
                        dueDateUtc.Value,
                        IsPreferredCategory(email, preferredCategories),
                        email.ActionRequired));
            }
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.IsPreferredCategory)
            .ThenByDescending(candidate => candidate.IsActionRequired)
            .ThenByDescending(candidate => candidate.DueDateUtc >= reminder.DueAtUtc)
            .ThenByDescending(candidate => candidate.Email.ReceivedAtUtc)
            .FirstOrDefault();

        return selected is null
            ? new ReminderEvidenceSelection(null, null, emailsUpdated, candidateEmails.Count, candidates.Count)
            : new ReminderEvidenceSelection(selected.Email, selected.DueDateUtc, emailsUpdated, candidateEmails.Count, candidates.Count);
    }

    private static bool IsPreferredCategory(InboxEmail email, IReadOnlyCollection<EmailCategory> preferredCategories)
    {
        return preferredCategories.Count == 0 ||
               email.Category.HasValue &&
               preferredCategories.Contains(email.Category.Value);
    }

    private static string SubjectForLog(string subject)
    {
        var normalized = subject
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return normalized.Length <= 120
            ? normalized
            : normalized[..117] + "...";
    }

    private sealed record ReminderEvidenceCandidate(
        InboxEmail Email,
        DateTimeOffset DueDateUtc,
        bool IsPreferredCategory,
        bool IsActionRequired);

    private sealed record ReminderEvidenceSelection(
        InboxEmail? Email,
        DateTimeOffset? DueDateUtc,
        int EmailsUpdated,
        int ScannedCandidateCount,
        int CandidateCountWithDueDate);
}
