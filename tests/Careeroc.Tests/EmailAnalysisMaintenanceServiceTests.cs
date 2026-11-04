using Careeroc.Application;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;

namespace Careeroc.Tests;

public sealed class EmailAnalysisMaintenanceServiceTests
{
    [Fact]
    public async Task QueueStaleEmailsAsync_RequeuesProcessedEmail_WhenAnalysisVersionIsOld()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 2, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "stale-analysis-001",
            ThreadId = "thread-stale-analysis-001",
            Sender = "recruiting@example.com",
            Subject = "Interview invitation",
            RawBody = "Please schedule your interview.",
            ParsedBody = "Please schedule your interview.",
            ReceivedAtUtc = clock.UtcNow.AddDays(-1),
            ProcessedAtUtc = clock.UtcNow.AddHours(-12),
            ProcessingNotes = "[analysis:old] Previous parser output."
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        dbContext.EmailProcessingJobs.Add(
            new EmailProcessingJob
            {
                InboxEmailId = email.Id,
                InboxEmail = email,
                Status = EmailProcessingStatus.Completed,
                CompletedAtUtc = clock.UtcNow.AddHours(-11)
            });
        await dbContext.SaveChangesAsync();

        var service = new EmailAnalysisMaintenanceService(
            dbContext,
            new DatabaseEmailProcessingQueue(dbContext, clock));

        var queued = await service.QueueStaleEmailsAsync();

        Assert.Equal(1, queued);
        var job = Assert.Single(dbContext.EmailProcessingJobs);
        Assert.Equal(email.Id, job.InboxEmailId);
        Assert.Equal(EmailProcessingStatus.Pending, job.Status);
    }

    [Fact]
    public async Task QueueStaleEmailsAsync_SkipsCurrentVersionAndManuallyReviewedEmails()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 2, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.AddRange(
            new InboxEmail
            {
                InboxAccountId = inboxAccount.Id,
                InboxAccount = inboxAccount,
                ExternalMessageId = "current-analysis-001",
                ThreadId = "thread-current-analysis-001",
                Sender = "recruiting@example.com",
                Subject = "Application update",
                RawBody = "Update",
                ParsedBody = "Update",
                ReceivedAtUtc = clock.UtcNow.AddDays(-1),
                ProcessedAtUtc = clock.UtcNow.AddHours(-12),
                ProcessingNotes = $"{EmailAnalysisVersion.Marker} Current parser output."
            },
            new InboxEmail
            {
                InboxAccountId = inboxAccount.Id,
                InboxAccount = inboxAccount,
                ExternalMessageId = "manual-review-001",
                ThreadId = "thread-manual-review-001",
                Sender = "recruiting@example.com",
                Subject = "Application update",
                RawBody = "Update",
                ParsedBody = "Update",
                ReceivedAtUtc = clock.UtcNow.AddDays(-1),
                ProcessedAtUtc = clock.UtcNow.AddHours(-12),
                ProcessingNotes = "[analysis:old] Manually corrected parser output.",
                ReviewedAtUtc = clock.UtcNow.AddHours(-10)
            });
        await dbContext.SaveChangesAsync();

        var service = new EmailAnalysisMaintenanceService(
            dbContext,
            new DatabaseEmailProcessingQueue(dbContext, clock));

        var queued = await service.QueueStaleEmailsAsync();

        Assert.Equal(0, queued);
        Assert.Empty(dbContext.EmailProcessingJobs);
    }

    [Fact]
    public async Task QueueStaleEmailsAsync_KeepsCurrentFailedJobDeadLettered()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 2, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "failed-current-analysis-001",
            ThreadId = "thread-failed-current-analysis-001",
            Sender = "recruiting@example.com",
            Subject = "Interview invitation",
            RawBody = "Please schedule your interview.",
            ParsedBody = "Please schedule your interview.",
            ReceivedAtUtc = clock.UtcNow.AddDays(-1),
            ProcessingNotes = "[analysis:old] Previous parser output."
        };
        var failedJob = new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = EmailProcessingStatus.Failed,
            Attempts = 3,
            LastError = $"{EmailAnalysisVersion.Marker} Parser failed for malformed body."
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        dbContext.EmailProcessingJobs.Add(failedJob);
        await dbContext.SaveChangesAsync();

        var service = new EmailAnalysisMaintenanceService(
            dbContext,
            new DatabaseEmailProcessingQueue(dbContext, clock));

        var queued = await service.QueueStaleEmailsAsync();

        Assert.Equal(0, queued);
        var job = Assert.Single(dbContext.EmailProcessingJobs);
        Assert.Equal(failedJob.Id, job.Id);
        Assert.Equal(EmailProcessingStatus.Failed, job.Status);
    }

    [Fact]
    public async Task QueueStaleEmailsAsync_RequeuesOldVersionFailedJob()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 2, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "failed-old-analysis-001",
            ThreadId = "thread-failed-old-analysis-001",
            Sender = "recruiting@example.com",
            Subject = "Interview invitation",
            RawBody = "Please schedule your interview.",
            ParsedBody = "Please schedule your interview.",
            ReceivedAtUtc = clock.UtcNow.AddDays(-1),
            ProcessingNotes = "[analysis:old] Previous parser output."
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        dbContext.EmailProcessingJobs.Add(
            new EmailProcessingJob
            {
                InboxEmailId = email.Id,
                InboxEmail = email,
                Status = EmailProcessingStatus.Failed,
                Attempts = 3,
                LastError = "[analysis:old] Parser failed for malformed body."
            });
        await dbContext.SaveChangesAsync();

        var service = new EmailAnalysisMaintenanceService(
            dbContext,
            new DatabaseEmailProcessingQueue(dbContext, clock));

        var queued = await service.QueueStaleEmailsAsync();

        Assert.Equal(1, queued);
        var job = Assert.Single(dbContext.EmailProcessingJobs);
        Assert.Equal(email.Id, job.InboxEmailId);
        Assert.Equal(EmailProcessingStatus.Pending, job.Status);
        Assert.Equal(0, job.Attempts);
    }
}
