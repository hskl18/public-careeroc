using System.Text.Json;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Applications;
using Careeroc.Contracts.Emails;
using Careeroc.Application.Services.Agents;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class ManualReviewService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly ApplicationWorkflowService _applicationWorkflowService;
    private readonly FollowUpTaskAgentService _followUpTaskAgentService;
    private readonly IRecruitingEmailGuidanceStore _guidanceStore;
    private readonly IClock _clock;

    public ManualReviewService(
        ICareerocDbContext dbContext,
        ApplicationWorkflowService applicationWorkflowService,
        IClock clock)
        : this(
            dbContext,
            applicationWorkflowService,
            new FollowUpTaskAgentService(clock),
            NullRecruitingEmailGuidanceStore.Instance,
            clock)
    {
    }

    public ManualReviewService(
        ICareerocDbContext dbContext,
        ApplicationWorkflowService applicationWorkflowService,
        FollowUpTaskAgentService followUpTaskAgentService,
        IClock clock)
        : this(
            dbContext,
            applicationWorkflowService,
            followUpTaskAgentService,
            NullRecruitingEmailGuidanceStore.Instance,
            clock)
    {
    }

    public ManualReviewService(
        ICareerocDbContext dbContext,
        ApplicationWorkflowService applicationWorkflowService,
        FollowUpTaskAgentService followUpTaskAgentService,
        IRecruitingEmailGuidanceStore guidanceStore,
        IClock clock)
    {
        _dbContext = dbContext;
        _applicationWorkflowService = applicationWorkflowService;
        _followUpTaskAgentService = followUpTaskAgentService;
        _guidanceStore = guidanceStore;
        _clock = clock;
    }

    public async Task<bool> CorrectEmailAsync(
        Guid inboxEmailId,
        CorrectEmailRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        if (resolvedUserEmail is null)
        {
            return false;
        }

        var inboxEmail = await _dbContext.InboxEmails
            .Include(email => email.InboxAccount)
            .FirstOrDefaultAsync(
                email =>
                    email.Id == inboxEmailId &&
                    email.InboxAccount.User.Email == resolvedUserEmail,
                cancellationToken);

        if (inboxEmail is null)
        {
            return false;
        }

        var previousCategory = inboxEmail.Category;
        var previousActionRequired = inboxEmail.ActionRequired;
        var previousDueDateUtc = inboxEmail.DueDateUtc;
        var previousCompany = inboxEmail.ExtractedCompany;
        var previousRole = inboxEmail.ExtractedRole;

        // "Keep unassigned" on an email that never had an application must not let the workflow
        // below match or create one.
        var explicitlyClearedAssignment =
            request.ClearApplicationAssignment &&
            request.JobApplicationId is null;

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            inboxEmail.Category = ParseEnum<EmailCategory>(request.Category);
        }

        if (explicitlyClearedAssignment)
        {
            inboxEmail.JobApplicationId = null;
        }
        else if (request.JobApplicationId.HasValue)
        {
            var applicationExists = await _dbContext.JobApplications
                .AnyAsync(
                    application =>
                        application.Id == request.JobApplicationId.Value &&
                        application.User.Email == resolvedUserEmail,
                    cancellationToken);

            if (!applicationExists)
            {
                return false;
            }

            inboxEmail.JobApplicationId = request.JobApplicationId.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Company))
        {
            inboxEmail.ExtractedCompany = request.Company.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            inboxEmail.ExtractedRole = request.Role.Trim();
        }

        if (request.ActionRequired.HasValue)
        {
            inboxEmail.ActionRequired = request.ActionRequired.Value;
        }

        if (request.ClearDueDate)
        {
            inboxEmail.DueDateUtc = null;
        }
        else if (request.DueDateUtc.HasValue)
        {
            inboxEmail.DueDateUtc = request.DueDateUtc.Value.NormalizeUtc();
        }

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            inboxEmail.ProcessingNotes = request.Notes.Trim();
        }

        await ReconcileWorkflowAsync(
            inboxEmail,
            explicitlyClearedAssignment,
            cancellationToken);

        inboxEmail.RequiresManualReview = false;
        inboxEmail.ReviewReason = null;
        inboxEmail.ReviewedAtUtc = _clock.UtcNow;
        inboxEmail.ProcessedAtUtc = _clock.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await TryAppendGuidanceFeedbackAsync(
            inboxEmail,
            request,
            previousCategory,
            previousActionRequired,
            previousDueDateUtc,
            previousCompany,
            previousRole,
            cancellationToken);
        return true;
    }

    public async Task<bool> UpdateApplicationStatusAsync(
        Guid jobApplicationId,
        UpdateApplicationStatusRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        if (resolvedUserEmail is null)
        {
            return false;
        }

        var jobApplication = await _dbContext.JobApplications
            .FirstOrDefaultAsync(
                application =>
                    application.Id == jobApplicationId &&
                    application.User.Email == resolvedUserEmail,
                cancellationToken);

        if (jobApplication is null)
        {
            return false;
        }

        var parsedStatus = ParseEnum<ApplicationStatus>(request.Status);
        jobApplication.Status = parsedStatus;
        jobApplication.LastActivityAtUtc = _clock.UtcNow;

        if (request.Priority.HasValue)
        {
            jobApplication.Priority = Math.Clamp(request.Priority.Value, 1, 5);
        }

        _dbContext.JobApplicationEvents.Add(new JobApplicationEvent
        {
            JobApplicationId = jobApplication.Id,
            EventType = "manual_stage_update",
            EventTimeUtc = _clock.UtcNow,
            ExtractedDataJson = JsonSerializer.Serialize(new { request.Status, request.Priority })
        });

        await _applicationWorkflowService.DismissStaleRemindersAsync(
            jobApplication,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CorrectApplicationAsync(
        Guid jobApplicationId,
        CorrectApplicationRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ResolveUserEmail(userEmail);
        if (resolvedUserEmail is null)
        {
            return false;
        }

        var correctionKind = NormalizeCorrectionKind(request.Kind);
        if (correctionKind != "not_an_application")
        {
            throw new InvalidOperationException("Unsupported application correction kind.");
        }

        var jobApplication = await _dbContext.JobApplications
            .Include(application => application.Emails)
            .Include(application => application.Reminders)
            .FirstOrDefaultAsync(
                application =>
                    application.Id == jobApplicationId &&
                    application.User.Email == resolvedUserEmail,
                cancellationToken);

        if (jobApplication is null)
        {
            return false;
        }

        var note = string.IsNullOrWhiteSpace(request.Notes)
            ? "User marked this record as not an application."
            : request.Notes.Trim();
        jobApplication.Status = ApplicationStatus.Closed;
        jobApplication.LastActivityAtUtc = _clock.UtcNow;

        foreach (var email in jobApplication.Emails)
        {
            email.JobApplicationId = null;
            email.MatchingConfidence = null;
            email.RequiresManualReview = true;
            email.ReviewReason = "Application record was marked as not an application.";
            email.ProcessingNotes = MergeNotes(email.ProcessingNotes, note);
        }

        _dbContext.JobApplicationEvents.Add(
            new JobApplicationEvent
            {
                JobApplicationId = jobApplication.Id,
                EventType = "not_an_application",
                EventTimeUtc = _clock.UtcNow,
                ExtractedDataJson = JsonSerializer.Serialize(new { kind = correctionKind, note })
            });

        await _applicationWorkflowService.DismissStaleRemindersAsync(
            jobApplication,
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? ResolveUserEmail(string? userEmail)
    {
        return string.IsNullOrWhiteSpace(userEmail)
            ? null
            : userEmail.Trim().ToLowerInvariant();
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum
    {
        var normalized = value
            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (Enum.TryParse<TEnum>(normalized, true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Unsupported value '{value}' for {typeof(TEnum).Name}.");
    }

    private static string NormalizeCorrectionKind(string value)
    {
        return value.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal);
    }

    private static string MergeNotes(string? existing, string addition)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return addition;
        }

        if (existing.Contains(addition, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        var merged = $"{existing.Trim()}\n{addition}";
        return merged.Length <= 2000 ? merged : merged[^2000..];
    }

    private async Task ReconcileWorkflowAsync(
        InboxEmail inboxEmail,
        bool explicitlyClearedAssignment,
        CancellationToken cancellationToken)
    {
        if (explicitlyClearedAssignment || !inboxEmail.Category.HasValue)
        {
            inboxEmail.MatchingConfidence = explicitlyClearedAssignment ? null : inboxEmail.MatchingConfidence;
            return;
        }

        var company = inboxEmail.ExtractedCompany?.Trim() ?? string.Empty;
        var role = inboxEmail.ExtractedRole?.Trim() ?? string.Empty;
        var category = inboxEmail.Category.Value;
        var match = await _applicationWorkflowService.MatchOrCreateApplicationAsync(
            inboxEmail.InboxAccount.UserId,
            inboxEmail.JobApplicationId,
            company,
            role,
            ApplicationWorkflowService.ToRecommendedStatus(category),
            category,
            inboxEmail.ReceivedAtUtc,
            allowAutoCreate: ApplicationWorkflowService.CanCreateApplication(company, role),
            cancellationToken);

        inboxEmail.JobApplicationId = match.Application?.Id;
        inboxEmail.MatchingConfidence = match.MatchingConfidence;

        if (match.Application is null)
        {
            return;
        }

        _applicationWorkflowService.ApplyStatusAndPriority(
            match.Application,
            category,
            ApplicationWorkflowService.ToRecommendedStatus(category),
            inboxEmail.ReceivedAtUtc);
        await _applicationWorkflowService.DismissStaleRemindersAsync(
            match.Application,
            cancellationToken);

        await _applicationWorkflowService.UpsertEmailEventAsync(
            match.Application,
            inboxEmail,
            ApplicationWorkflowService.ToApiLabel(category.ToString()),
            inboxEmail.ReceivedAtUtc,
            new
            {
                category,
                Company = inboxEmail.ExtractedCompany,
                Role = inboxEmail.ExtractedRole,
                inboxEmail.DueDateUtc,
                inboxEmail.ActionRequired,
                inboxEmail.ClassificationConfidence,
                match.MatchingConfidence,
                manuallyCorrected = true,
                inboxEmail.ProcessingNotes
            },
            cancellationToken);

        if (inboxEmail.ActionRequired &&
            !ApplicationWorkflowService.IsTerminalStatus(match.Application.Status))
        {
            var followUpTaskPlan = _followUpTaskAgentService.CreatePlan(
                match.Application,
                new ParsedRecruitingEmail(
                    category,
                    ApplicationWorkflowService.ToRecommendedStatus(category),
                    inboxEmail.ExtractedCompany ?? ApplicationDefaults.UnknownCompany,
                    inboxEmail.ExtractedRole ?? ApplicationDefaults.UnknownRole,
                    inboxEmail.ActionRequired,
                    inboxEmail.DueDateUtc,
                    inboxEmail.ClassificationConfidence ?? 0m,
                    inboxEmail.ProcessingNotes ?? "Manual review correction planned a follow-up task.",
                    "Manual Review",
                    null),
                inboxEmail.DueDateUtc);
            await _applicationWorkflowService.EnsureReminderAsync(
                match.Application,
                followUpTaskPlan,
                cancellationToken);
        }
    }

    private async Task TryAppendGuidanceFeedbackAsync(
        InboxEmail inboxEmail,
        CorrectEmailRequest request,
        EmailCategory? previousCategory,
        bool previousActionRequired,
        DateTimeOffset? previousDueDateUtc,
        string? previousCompany,
        string? previousRole,
        CancellationToken cancellationToken)
    {
        if (!HasGuidanceSignal(request, previousCategory, previousCompany, previousRole, inboxEmail))
        {
            return;
        }

        try
        {
            await _guidanceStore.AppendFeedbackAsync(
                new RecruitingEmailGuidanceFeedback(
                    _clock.UtcNow,
                    inboxEmail.Sender,
                    inboxEmail.Subject,
                    previousCategory,
                    inboxEmail.Category,
                    previousActionRequired,
                    inboxEmail.ActionRequired,
                    previousDueDateUtc,
                    inboxEmail.DueDateUtc,
                    previousCompany,
                    inboxEmail.ExtractedCompany,
                    previousRole,
                    inboxEmail.ExtractedRole,
                    request.Notes),
                cancellationToken);
        }
        catch
        {
            // Guidance memory is a local harness aid. A file-write issue must not block the user's correction.
        }
    }

    private static bool HasGuidanceSignal(
        CorrectEmailRequest request,
        EmailCategory? previousCategory,
        string? previousCompany,
        string? previousRole,
        InboxEmail inboxEmail)
    {
        if (previousCategory != inboxEmail.Category)
        {
            return true;
        }

        if (!string.Equals(previousCompany, inboxEmail.ExtractedCompany, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(previousRole, inboxEmail.ExtractedRole, StringComparison.Ordinal))
        {
            return true;
        }

        return request.ActionRequired.HasValue ||
               request.ClearDueDate ||
               request.DueDateUtc.HasValue ||
               !string.IsNullOrWhiteSpace(request.Notes);
    }
}
