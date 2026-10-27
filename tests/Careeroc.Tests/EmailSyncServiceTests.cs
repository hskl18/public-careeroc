using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace Careeroc.Tests;

public sealed class EmailSyncServiceTests
{
    [Fact]
    public async Task RunOnceAsync_OnRepeatSync_PassesRecentMessageIdsAndActiveKnownThreads()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "jlee@example.edu",
            Name = "Jordan Lee"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email,
            LastSyncedAtUtc = clock.UtcNow
        };
        InboxAccountMetadataJson.Write(
            inboxAccount,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                InitialBackfillCompletedAtUtc = clock.UtcNow.AddDays(-30),
                SentReplyBackfillCompletedAtUtc = clock.UtcNow.AddDays(-30)
            });

        var activeApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Stripe",
            Role = "Software Engineer Intern",
            Source = "Email",
            Status = ApplicationStatus.Interviewing,
            Priority = 5,
            LastActivityAtUtc = clock.UtcNow.AddDays(-30),
            CreatedAtUtc = clock.UtcNow.AddDays(-35)
        };
        var closedApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Archived Co",
            Role = "Software Engineer Intern",
            Source = "Email",
            Status = ApplicationStatus.Closed,
            Priority = 1,
            LastActivityAtUtc = clock.UtcNow.AddDays(-30),
            CreatedAtUtc = clock.UtcNow.AddDays(-35)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.AddRange(activeApplication, closedApplication);
        dbContext.InboxEmails.AddRange(
            new InboxEmail
            {
                InboxAccountId = inboxAccount.Id,
                InboxAccount = inboxAccount,
                ExternalMessageId = "recent-message",
                ThreadId = "recent-thread",
                Sender = "Recruiter <recruiting@veeva.com>",
                Subject = "Recent recruiting note",
                ParsedBody = "Recent recruiting note",
                RawBody = "Recent recruiting note",
                ReceivedAtUtc = new DateTimeOffset(2026, 4, 22, 9, 0, 0, TimeSpan.Zero)
            },
            new InboxEmail
            {
                InboxAccountId = inboxAccount.Id,
                InboxAccount = inboxAccount,
                ExternalMessageId = "older-unlinked-message",
                ThreadId = "older-unlinked-thread",
                Sender = "Recruiter <recruiting@stripe.com>",
                Subject = "Older recruiting note",
                ParsedBody = "Older recruiting note",
                RawBody = "Older recruiting note",
                ReceivedAtUtc = new DateTimeOffset(2026, 4, 21, 23, 0, 0, TimeSpan.Zero)
            },
            new InboxEmail
            {
                InboxAccountId = inboxAccount.Id,
                InboxAccount = inboxAccount,
                JobApplicationId = activeApplication.Id,
                JobApplication = activeApplication,
                ExternalMessageId = "older-active-message",
                ThreadId = "older-active-thread",
                Sender = "Recruiter <recruiting@stripe.com>",
                Subject = "Older active recruiting note",
                ParsedBody = "Older active recruiting note",
                RawBody = "Older active recruiting note",
                ReceivedAtUtc = new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.Zero)
            },
            new InboxEmail
            {
                InboxAccountId = inboxAccount.Id,
                InboxAccount = inboxAccount,
                JobApplicationId = closedApplication.Id,
                JobApplication = closedApplication,
                ExternalMessageId = "older-closed-message",
                ThreadId = "older-closed-thread",
                Sender = "Recruiter <recruiting@archived.example>",
                Subject = "Older closed recruiting note",
                ParsedBody = "Older closed recruiting note",
                RawBody = "Older closed recruiting note",
                ReceivedAtUtc = new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();

        var source = new CapturingInboxEmailSource();
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        var service = new EmailSyncService(
            dbContext,
            source,
            queue,
            clock,
            NullLogger<EmailSyncService>.Instance);

        var result = await service.RunOnceAsync((string?)null);

        Assert.Equal(0, result.EmailsCreated);
        Assert.Equal(0, result.JobsEnqueued);
        Assert.NotNull(source.CapturedExistingMessageIds);
        Assert.NotNull(source.CapturedExistingThreadIds);
        Assert.Contains("recent-message", source.CapturedExistingMessageIds!, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("older-active-message", source.CapturedExistingMessageIds!, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("older-unlinked-message", source.CapturedExistingMessageIds!, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("older-closed-message", source.CapturedExistingMessageIds!, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("recent-thread", source.CapturedExistingThreadIds!, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("older-active-thread", source.CapturedExistingThreadIds!, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("older-unlinked-thread", source.CapturedExistingThreadIds!, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("older-closed-thread", source.CapturedExistingThreadIds!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunOnceAsync_LinksOutboundReplyAndCompletesReplyAction()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "jlee@example.edu",
            Name = "Jordan Lee"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        InboxAccountMetadataJson.Write(
            inboxAccount,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                AccessToken = "access-token",
                RefreshToken = "refresh-token"
            });

        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Veeva Systems",
            Role = "Software Engineering Internship",
            Source = "Email",
            Status = ApplicationStatus.RecruiterContact,
            Priority = 3,
            LastActivityAtUtc = clock.UtcNow.AddDays(-1),
            CreatedAtUtc = clock.UtcNow.AddDays(-2)
        };
        var inboundEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "inbound-1",
            ThreadId = "thread-veeva",
            Sender = "Recruiter <recruiting@veeva.com>",
            Subject = "Please reply with your availability",
            ParsedBody = "Please reply with your availability.",
            RawBody = "Please reply with your availability.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-3),
            ActionRequired = true,
            DueDateUtc = clock.UtcNow.AddDays(1),
            ProcessingNotes = "Created reply reminder."
        };
        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.FollowUp,
            Title = "Reply with availability",
            DueAtUtc = clock.UtcNow.AddDays(1),
            Status = ReminderStatus.Open
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(inboundEmail);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        var outboundReply = new ExternalInboxEmail(
            "sent-1",
            inboundEmail.ThreadId,
            "Jordan Lee <jlee@example.edu>",
            "Re: Please reply with your availability",
            "Thanks, I can do Tuesday afternoon.",
            clock.UtcNow.AddHours(-1),
            IsOutbound: true);
        var source = new FixedInboxEmailSource([outboundReply]);
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        var service = new EmailSyncService(
            dbContext,
            source,
            queue,
            clock,
            NullLogger<EmailSyncService>.Instance);

        var result = await service.RunOnceAsync((string?)null);

        Assert.Equal(1, result.EmailsCreated);
        Assert.Equal(0, result.JobsEnqueued);
        Assert.Equal(ReminderStatus.Completed, reminder.Status);
        Assert.False(inboundEmail.ActionRequired);
        Assert.Null(inboundEmail.DueDateUtc);
        Assert.Contains("sent Gmail reply", inboundEmail.ProcessingNotes, StringComparison.OrdinalIgnoreCase);

        var storedOutbound = Assert.Single(dbContext.InboxEmails.Where(email => email.ExternalMessageId == "sent-1"));
        Assert.True(storedOutbound.IsOutbound);
        Assert.Equal(application.Id, storedOutbound.JobApplicationId);
    }

    [Fact]
    public async Task RunOnceAsync_DeduplicatesSourceEmailsWithinSingleSync()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 27, 22, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "jordan.lee@example.edu",
            Name = "Jordan Lee"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        InboxAccountMetadataJson.Write(
            inboxAccount,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                AccessToken = "access-token",
                RefreshToken = "refresh-token"
            });

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        await dbContext.SaveChangesAsync();

        var duplicateEmail = new ExternalInboxEmail(
            "gmail-message-1",
            "gmail-thread-1",
            "Recruiter <recruiting@example.com>",
            "Application update",
            "Thanks for applying.",
            clock.UtcNow.AddHours(-1),
            IsOutbound: false);
        var source = new FixedInboxEmailSource([duplicateEmail, duplicateEmail]);
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        var service = new EmailSyncService(
            dbContext,
            source,
            queue,
            clock,
            NullLogger<EmailSyncService>.Instance);

        var result = await service.RunOnceAsync((string?)null);

        Assert.Equal(1, result.EmailsCreated);
        Assert.Equal(1, result.JobsEnqueued);
        Assert.Equal(clock.UtcNow, inboxAccount.LastSyncedAtUtc);
        Assert.Single(dbContext.InboxEmails.Where(email => email.ExternalMessageId == "gmail-message-1"));
    }

    [Fact]
    public async Task RunOnceAsync_ClipsOversizedHeadersToTheirColumns()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 27, 22, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var user = new User { Email = "jordan.lee@example.edu", Name = "Jordan Lee" };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        InboxAccountMetadataJson.Write(
            inboxAccount,
            new InboxAccountMetadata { SetupMode = "oauth", ConnectionStatus = "Connected", AccessToken = "access-token" });
        dbContext.AddRange(user, inboxAccount);
        await dbContext.SaveChangesAsync();
        var source = new FixedInboxEmailSource(
        [
            new ExternalInboxEmail(
                "gmail-message-long",
                "gmail-thread-long",
                new string('s', 400) + "@example.com",
                new string('x', 900),
                "Thanks for applying.",
                clock.UtcNow.AddHours(-1),
                IsOutbound: false)
        ]);
        var service = new EmailSyncService(
            dbContext,
            source,
            new DatabaseEmailProcessingQueue(dbContext, clock),
            clock,
            NullLogger<EmailSyncService>.Instance);

        await service.RunOnceAsync((string?)null);

        var stored = Assert.Single(dbContext.InboxEmails);
        Assert.Equal(320, stored.Sender.Length);
        Assert.Equal(500, stored.Subject.Length);
    }

    [Fact]
    public async Task RunOnceAsync_SkipsInbox_WhenSyncLeaseIsUnavailable()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "jlee@example.edu",
            Name = "Jordan Lee"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        InboxAccountMetadataJson.Write(
            inboxAccount,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                AccessToken = "access-token",
                RefreshToken = "refresh-token"
            });

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        await dbContext.SaveChangesAsync();

        var source = new CapturingInboxEmailSource();
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        var service = new EmailSyncService(
            dbContext,
            source,
            queue,
            new DenyingInboxSyncLeaseManager(),
            clock,
            NullLogger<EmailSyncService>.Instance);

        var result = await service.RunOnceAsync((string?)null);

        Assert.Equal(0, result.EmailsCreated);
        Assert.Equal(0, result.JobsEnqueued);
        Assert.Equal(EmailSyncStatuses.LeaseBusy, result.Status);
        Assert.Equal(0, source.CallCount);
        Assert.Null(inboxAccount.LastSyncedAtUtc);
    }

    [Fact]
    public async Task RunOnceAsync_ReturnsAuthRequired_WithoutAdvancingLastSynced_WhenSourceRequiresReconnect()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "jlee@example.edu",
            Name = "Jordan Lee"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        InboxAccountMetadataJson.Write(
            inboxAccount,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                AccessToken = "access-token",
                RefreshToken = "refresh-token"
            });

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        var service = new EmailSyncService(
            dbContext,
            new ReconnectRequiredInboxEmailSource(),
            queue,
            clock,
            NullLogger<EmailSyncService>.Instance);

        var result = await service.RunOnceAsync((string?)null);

        Assert.Equal(0, result.EmailsCreated);
        Assert.Equal(0, result.JobsEnqueued);
        Assert.Equal(EmailSyncStatuses.AuthRequired, result.Status);
        Assert.Equal("Reconnect Gmail.", result.Message);
        Assert.Null(inboxAccount.LastSyncedAtUtc);
    }

    private sealed class CapturingInboxEmailSource : Careeroc.Application.Abstractions.IInboxEmailSource
    {
        public IReadOnlySet<string>? CapturedExistingMessageIds { get; private set; }
        public IReadOnlySet<string>? CapturedExistingThreadIds { get; private set; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
            InboxAccount inboxAccount,
            IReadOnlySet<string>? existingMessageIds = null,
            IReadOnlySet<string>? existingThreadIds = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CapturedExistingMessageIds = existingMessageIds;
            CapturedExistingThreadIds = existingThreadIds;
            return Task.FromResult<IReadOnlyList<ExternalInboxEmail>>([]);
        }
    }

    private sealed class FixedInboxEmailSource : Careeroc.Application.Abstractions.IInboxEmailSource
    {
        private readonly IReadOnlyList<ExternalInboxEmail> _emails;

        public FixedInboxEmailSource(IReadOnlyList<ExternalInboxEmail> emails)
        {
            _emails = emails;
        }

        public Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
            InboxAccount inboxAccount,
            IReadOnlySet<string>? existingMessageIds = null,
            IReadOnlySet<string>? existingThreadIds = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_emails);
        }
    }

    private sealed class ReconnectRequiredInboxEmailSource : Careeroc.Application.Abstractions.IInboxEmailSource
    {
        public Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
            InboxAccount inboxAccount,
            IReadOnlySet<string>? existingMessageIds = null,
            IReadOnlySet<string>? existingThreadIds = null,
            CancellationToken cancellationToken = default)
        {
            var metadata = InboxAccountMetadataJson.Read(inboxAccount, _ => new InboxAccountMetadata());
            metadata.ConnectionStatus = "Reconnect required";
            metadata.NextStep = "Reconnect Gmail.";
            InboxAccountMetadataJson.Write(inboxAccount, metadata);
            return Task.FromResult<IReadOnlyList<ExternalInboxEmail>>([]);
        }
    }

    private sealed class DenyingInboxSyncLeaseManager : Careeroc.Application.Abstractions.ILeaseManager
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }
    }
}
