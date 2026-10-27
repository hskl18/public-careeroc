using Careeroc.Application.Abstractions;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class EmailAnalysisMaintenanceService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IEmailProcessingQueue _emailProcessingQueue;

    public EmailAnalysisMaintenanceService(
        ICareerocDbContext dbContext,
        IEmailProcessingQueue emailProcessingQueue)
    {
        _dbContext = dbContext;
        _emailProcessingQueue = emailProcessingQueue;
    }

    public async Task<int> QueueStaleEmailsAsync(
        string? userEmail = null,
        int maxEmails = 1000,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.InboxEmails
            .Where(
                email =>
                    !email.IsOutbound &&
                    !email.ReviewedAtUtc.HasValue &&
                    (!email.ProcessedAtUtc.HasValue ||
                     email.ProcessingNotes == null ||
                     !email.ProcessingNotes.Contains(EmailAnalysisVersion.Marker)));

        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            var resolvedUserEmail = userEmail.Trim().ToLowerInvariant();
            query = query.Where(email => email.InboxAccount.User.Email == resolvedUserEmail);
        }

        var emailIds = await query
            .OrderBy(email => email.ReceivedAtUtc)
            .Select(email => email.Id)
            .Take(maxEmails)
            .ToListAsync(cancellationToken);

        if (emailIds.Count == 0)
        {
            return 0;
        }

        var terminalJobs = await _dbContext.EmailProcessingJobs
            .Where(
                job =>
                    emailIds.Contains(job.InboxEmailId) &&
                    (job.Status == EmailProcessingStatus.Completed ||
                     job.Status == EmailProcessingStatus.Failed))
            .ToListAsync(cancellationToken);
        var currentFailedEmailIds = terminalJobs
            .Where(job => job.Status == EmailProcessingStatus.Failed && HasCurrentAnalysisFailure(job))
            .Select(job => job.InboxEmailId)
            .ToHashSet();
        var jobsToClear = terminalJobs
            .Where(job => job.Status == EmailProcessingStatus.Completed || !HasCurrentAnalysisFailure(job))
            .ToList();

        if (jobsToClear.Count > 0)
        {
            _dbContext.EmailProcessingJobs.RemoveRange(jobsToClear);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var queued = 0;
        foreach (var emailId in emailIds)
        {
            if (currentFailedEmailIds.Contains(emailId))
            {
                continue;
            }

            queued += await _emailProcessingQueue.EnqueueAsync(emailId, cancellationToken);
        }

        return queued;
    }

    private static bool HasCurrentAnalysisFailure(Domain.Entities.EmailProcessingJob job)
    {
        return !string.IsNullOrWhiteSpace(job.LastError) &&
               job.LastError.Contains(EmailAnalysisVersion.Marker, StringComparison.Ordinal);
    }
}
