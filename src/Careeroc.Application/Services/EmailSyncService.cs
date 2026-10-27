using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Careeroc.Application.Services;

public sealed class EmailSyncService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IInboxEmailSource _inboxEmailSource;
    private readonly IEmailProcessingQueue _emailProcessingQueue;
    private readonly ILeaseManager _leaseManager;
    private readonly IClock _clock;
    private readonly ILogger<EmailSyncService> _logger;

    public EmailSyncService(
        ICareerocDbContext dbContext,
        IInboxEmailSource inboxEmailSource,
        IEmailProcessingQueue emailProcessingQueue,
        IClock clock,
        ILogger<EmailSyncService> logger)
        : this(
            dbContext,
            inboxEmailSource,
            emailProcessingQueue,
            NoopLeaseManager.Instance,
            clock,
            logger)
    {
    }

    [ActivatorUtilitiesConstructor]
    public EmailSyncService(
        ICareerocDbContext dbContext,
        IInboxEmailSource inboxEmailSource,
        IEmailProcessingQueue emailProcessingQueue,
        ILeaseManager leaseManager,
        IClock clock,
        ILogger<EmailSyncService> logger)
    {
        _dbContext = dbContext;
        _inboxEmailSource = inboxEmailSource;
        _emailProcessingQueue = emailProcessingQueue;
        _leaseManager = leaseManager;
        _clock = clock;
        _logger = logger;
    }

    public Task<EmailSyncResult> RunOnceAsync(
        string? userEmail = null,
        CancellationToken cancellationToken = default)
    {
        return RunOnceAsync(userEmail, null, cancellationToken);
    }

    public async Task<EmailSyncResult> RunOnceAsync(
        string? userEmail,
        TimeSpan? minimumSyncInterval,
        CancellationToken cancellationToken = default)
    {
        using var correlationScope = CareerocTelemetry.BeginCorrelationScope(runId: Guid.NewGuid().ToString("N"));
        using var activity = CareerocTelemetry.ActivitySource.StartActivity("email.sync_run");
        CareerocTelemetry.TagCurrentActivity();
        activity?.SetTag("user.email", userEmail);
        activity?.SetTag("sync.minimum_interval_seconds", minimumSyncInterval?.TotalSeconds);

        var inboxAccounts = await GetInboxAccountsAsync(userEmail, cancellationToken);

        if (minimumSyncInterval.HasValue)
        {
            var cacheCutoffUtc = _clock.UtcNow.Subtract(minimumSyncInterval.Value);
            inboxAccounts = inboxAccounts
                .Where(
                    account =>
                        !account.LastSyncedAtUtc.HasValue ||
                        account.LastSyncedAtUtc.Value <= cacheCutoffUtc)
                .ToList();
        }

        var totalEmailsCreated = 0;
        var totalJobsEnqueued = 0;
        var status = EmailSyncStatuses.Success;
        string? message = null;

        foreach (var inboxAccount in inboxAccounts)
        {
            var result = await SyncInboxAccountAsync(inboxAccount, cancellationToken);
            totalEmailsCreated += result.EmailsCreated;
            totalJobsEnqueued += result.JobsEnqueued;
            if (result.Status != EmailSyncStatuses.Success)
            {
                status = status == EmailSyncStatuses.Success
                    ? result.Status
                    : EmailSyncStatuses.Partial;
                message ??= result.Message;
            }
        }

        activity?.SetTag("sync.emails_created", totalEmailsCreated);
        activity?.SetTag("sync.jobs_enqueued", totalJobsEnqueued);
        activity?.SetTag("sync.status", status);

        return new EmailSyncResult(totalEmailsCreated, totalJobsEnqueued, status, message);
    }

    public async Task<EmailSyncResult?> RunInboxOnceAsync(
        Guid inboxAccountId,
        CancellationToken cancellationToken = default)
    {
        var inboxAccount = await _dbContext.InboxAccounts
            .FirstOrDefaultAsync(account => account.Id == inboxAccountId, cancellationToken);

        if (inboxAccount is null)
        {
            return null;
        }

        if (!CanPullFromInbox(inboxAccount))
        {
            return new EmailSyncResult(0, 0, EmailSyncStatuses.Skipped, "Inbox is not configured for provider sync.");
        }

        return await SyncInboxAccountAsync(inboxAccount, cancellationToken);
    }

    private async Task<EmailSyncResult> SyncInboxAccountAsync(
        InboxAccount inboxAccount,
        CancellationToken cancellationToken)
    {
        using var activity = CareerocTelemetry.ActivitySource.StartActivity("email.sync_inbox");
        activity?.SetTag("inbox.account_id", inboxAccount.Id.ToString());
        activity?.SetTag("inbox.provider", inboxAccount.Provider);

        try
        {
            await using var syncLease = await _leaseManager.TryAcquireAsync(
                $"inbox-sync:{inboxAccount.Id:N}",
                cancellationToken);
            if (syncLease is null)
            {
                _logger.LogInformation(
                    "Skipped email sync for inbox {EmailAddress} because another worker holds the sync lease.",
                    inboxAccount.EmailAddress);
                return new EmailSyncResult(0, 0, EmailSyncStatuses.LeaseBusy, "Another worker holds the inbox sync lease.");
            }

            var existingLookup = await LoadExistingMessageLookupAsync(inboxAccount, cancellationToken);
            var existingThreadLookup = await LoadExistingThreadLookupAsync(inboxAccount, cancellationToken);
            var sourceEmails = await _inboxEmailSource.GetRecentEmailsAsync(
                inboxAccount,
                existingLookup,
                existingThreadLookup,
                cancellationToken);
            var metadata = InboxAccountMetadataJson.Read(inboxAccount, _ => new InboxAccountMetadata(), _logger);
            if (IsReconnectRequired(metadata))
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new EmailSyncResult(
                    0,
                    0,
                    EmailSyncStatuses.AuthRequired,
                    metadata.NextStep);
            }

            var seenMessageIds = existingLookup.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var createdEmails = new List<InboxEmail>();

            foreach (var sourceEmail in sourceEmails)
            {
                if (!seenMessageIds.Add(sourceEmail.ExternalMessageId))
                {
                    continue;
                }

                createdEmails.Add(new InboxEmail
                {
                    InboxAccountId = inboxAccount.Id,
                    ExternalMessageId = sourceEmail.ExternalMessageId,
                    ThreadId = sourceEmail.ThreadId,
                    // Header values come from whoever sent the mail; an oversized one must not fail the sync.
                    Sender = Clip(sourceEmail.Sender, 320),
                    Subject = Clip(sourceEmail.Subject, 500),
                    RawBody = sourceEmail.Body,
                    ParsedBody = sourceEmail.Body,
                    ReceivedAtUtc = sourceEmail.ReceivedAtUtc,
                    IsOutbound = sourceEmail.IsOutbound,
                    ActionRequired = false,
                    ProcessedAtUtc = sourceEmail.IsOutbound ? _clock.UtcNow : null,
                    ProcessingNotes = sourceEmail.IsOutbound
                        ? "Detected a sent Gmail reply in this recruiting thread."
                        : null
                });
            }

            foreach (var outboundEmail in createdEmails.Where(email => email.IsOutbound))
            {
                await LinkOutboundReplyAsync(outboundEmail, cancellationToken);
            }

            if (createdEmails.Count > 0)
            {
                await _dbContext.InboxEmails.AddRangeAsync(createdEmails, cancellationToken);
            }

            inboxAccount.LastSyncedAtUtc = _clock.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var jobsEnqueued = 0;
            foreach (var email in createdEmails.Where(email => !email.IsOutbound))
            {
                jobsEnqueued += await _emailProcessingQueue.EnqueueAsync(email.Id, cancellationToken);
            }

            _logger.LogInformation(
                "Email sync completed for inbox {EmailAddress}. Created {EmailCount} emails and enqueued {JobsEnqueued} jobs.",
                inboxAccount.EmailAddress,
                createdEmails.Count,
                jobsEnqueued);

            return new EmailSyncResult(createdEmails.Count, jobsEnqueued);
        }
        catch (Exception exception)
        {
            // The next inbox in this run shares the context and must not resave this inbox's rows.
            _dbContext.ChangeTracker.DiscardPendingChanges();
            _logger.LogWarning(
                exception,
                "Email sync failed for inbox {EmailAddress}.",
                inboxAccount.EmailAddress);
            return new EmailSyncResult(0, 0, EmailSyncStatuses.Failed, "Email sync failed before completion.");
        }
    }

    private static string Clip(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool IsReconnectRequired(InboxAccountMetadata metadata)
    {
        return string.Equals(metadata.ConnectionStatus, "Reconnect required", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HashSet<string>> LoadExistingMessageLookupAsync(
        InboxAccount inboxAccount,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.InboxEmails
            .AsNoTracking()
            .Where(email => email.InboxAccountId == inboxAccount.Id);

        var messageCutoffUtc = ResolveExistingMessageLookupCutoffUtc(inboxAccount);
        if (messageCutoffUtc.HasValue)
        {
            query = query.Where(
                email =>
                    email.ReceivedAtUtc >= messageCutoffUtc.Value ||
                    (email.JobApplication != null &&
                     email.JobApplication.Status != ApplicationStatus.Rejected &&
                     email.JobApplication.Status != ApplicationStatus.Closed));
        }

        var existingMessageIds = await query
            .Select(email => email.ExternalMessageId)
            .ToListAsync(cancellationToken);

        return existingMessageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<HashSet<string>> LoadExistingThreadLookupAsync(
        InboxAccount inboxAccount,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.InboxEmails
            .AsNoTracking()
            .Where(email => email.InboxAccountId == inboxAccount.Id);

        var threadCutoffUtc = ResolveExistingMessageLookupCutoffUtc(inboxAccount);
        if (threadCutoffUtc.HasValue)
        {
            query = query.Where(
                email =>
                    email.ReceivedAtUtc >= threadCutoffUtc.Value ||
                    (email.JobApplication != null &&
                     email.JobApplication.Status != ApplicationStatus.Rejected &&
                     email.JobApplication.Status != ApplicationStatus.Closed));
        }

        var existingThreadIds = await query
            .Select(email => email.ThreadId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return existingThreadIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ResolveExistingMessageLookupCutoffUtc(InboxAccount inboxAccount)
    {
        if (!inboxAccount.LastSyncedAtUtc.HasValue)
        {
            return null;
        }

        // Gmail search queries are date-based (`after:yyyy/MM/dd`), so the lookup
        // needs to cover the whole UTC day that the sync window starts on.
        var syncWindowStartUtc = inboxAccount.LastSyncedAtUtc.Value.ToUniversalTime().AddDays(-1);
        return new DateTimeOffset(syncWindowStartUtc.Year, syncWindowStartUtc.Month, syncWindowStartUtc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private async Task LinkOutboundReplyAsync(
        InboxEmail outboundEmail,
        CancellationToken cancellationToken)
    {
        var linkedInboundEmails = await _dbContext.InboxEmails
            .Where(
                email =>
                    email.InboxAccountId == outboundEmail.InboxAccountId &&
                    email.ThreadId == outboundEmail.ThreadId &&
                    !email.IsOutbound &&
                    email.JobApplicationId.HasValue)
            .OrderByDescending(email => email.ReceivedAtUtc)
            .ToListAsync(cancellationToken);
        var applicationId = linkedInboundEmails
            .Select(email => email.JobApplicationId)
            .FirstOrDefault();

        if (!applicationId.HasValue)
        {
            return;
        }

        outboundEmail.JobApplicationId = applicationId.Value;

        foreach (var inboundEmail in linkedInboundEmails.Where(email => email.JobApplicationId == applicationId.Value))
        {
            inboundEmail.ActionRequired = false;
            inboundEmail.DueDateUtc = null;
            inboundEmail.ProcessingNotes = AppendProcessingNote(
                inboundEmail.ProcessingNotes,
                "Detected a sent Gmail reply in this thread, so the reply action is complete.");
        }

        var replyReminders = await _dbContext.Reminders
            .Where(
                reminder =>
                    reminder.JobApplicationId == applicationId.Value &&
                    reminder.Status == ReminderStatus.Open &&
                    (reminder.Type == ReminderType.FollowUp ||
                     reminder.Title.ToLower().StartsWith("reply") ||
                     reminder.Title.ToLower().StartsWith("confirm")))
            .ToListAsync(cancellationToken);

        foreach (var reminder in replyReminders)
        {
            reminder.Status = ReminderStatus.Completed;
        }

        if (replyReminders.Count > 0)
        {
            _dbContext.JobApplicationEvents.Add(new JobApplicationEvent
            {
                JobApplicationId = applicationId.Value,
                InboxEmailId = outboundEmail.Id,
                EventType = "gmail_reply_detected",
                EventTimeUtc = outboundEmail.ReceivedAtUtc,
                ExtractedDataJson = "{}"
            });
        }
    }

    private static string AppendProcessingNote(string? currentNotes, string note)
    {
        return string.IsNullOrWhiteSpace(currentNotes)
            ? note
            : $"{currentNotes} {note}";
    }

    public Task<EmailSyncResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        return RunOnceAsync(null, null, cancellationToken);
    }

    private async Task<IReadOnlyList<InboxAccount>> GetInboxAccountsAsync(
        string? userEmail,
        CancellationToken cancellationToken)
    {
        var accountsQuery = _dbContext.InboxAccounts.AsQueryable();

        var resolvedUserEmail = ResolveUserEmail(userEmail);
        if (!string.IsNullOrWhiteSpace(resolvedUserEmail))
        {
            accountsQuery = accountsQuery.Where(account => account.User.Email == resolvedUserEmail);
        }

        var accounts = await accountsQuery.ToListAsync(cancellationToken);

        return accounts
            .Where(CanPullFromInbox)
            .OrderBy(account => account.Provider == ApplicationDefaults.GmailInboxProvider ? 0 : 1)
            .ThenByDescending(account => account.LastSyncedAtUtc ?? account.CreatedAtUtc)
            .ToList();
    }

    internal static bool CanPullFromInbox(InboxAccount account)
    {
        if (account.Provider == ApplicationDefaults.DemoInboxProvider)
        {
            return true;
        }

        if (account.Provider != ApplicationDefaults.GmailInboxProvider)
        {
            return false;
        }

        var metadata = InboxAccountMetadataJson.Read(
            account,
            _ => new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Reconnect required",
                NextStep = "Reconnect Gmail to restore Gmail API access."
            });

        return !string.IsNullOrWhiteSpace(metadata.AccessToken) ||
               !string.IsNullOrWhiteSpace(metadata.RefreshToken);
    }

    private static string? ResolveUserEmail(string? userEmail)
    {
        return string.IsNullOrWhiteSpace(userEmail)
            ? null
            : userEmail.Trim().ToLowerInvariant();
    }
}
