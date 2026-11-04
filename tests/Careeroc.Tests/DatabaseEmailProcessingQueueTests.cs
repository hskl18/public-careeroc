using Careeroc.Application;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Configuration;

namespace Careeroc.Tests;

public sealed class DatabaseEmailProcessingQueueTests
{
    [Fact]
    public async Task DequeuePendingBatchAsync_RecoversStaleProcessingJob()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var email = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        var job = new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = EmailProcessingStatus.Processing,
            Attempts = 1,
            AvailableAtUtc = clock.UtcNow.AddDays(-1),
            LockedAtUtc = clock.UtcNow.AddHours(-3),
            CreatedAtUtc = clock.UtcNow.AddHours(-3),
            UpdatedAtUtc = clock.UtcNow.AddHours(-3)
        };
        dbContext.EmailProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);

        var jobs = await queue.DequeuePendingBatchAsync(5, "jordan.lee@example.edu");

        var dequeued = Assert.Single(jobs);
        Assert.Equal(job.Id, dequeued.Id);
        Assert.Equal(EmailProcessingStatus.Processing, job.Status);
        Assert.Equal(2, job.Attempts);
        Assert.Equal(clock.UtcNow, job.LockedAtUtc);
        Assert.Equal(clock.UtcNow, job.UpdatedAtUtc);
        Assert.Contains("Recovered stale processing lock", job.LastError);
    }

    [Fact]
    public async Task DequeuePendingBatchAsync_FailsAJobThatTimedOutOnItsLastAttempt()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var email = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        var job = new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = EmailProcessingStatus.Processing,
            Attempts = 3,
            AvailableAtUtc = clock.UtcNow.AddDays(-1),
            LockedAtUtc = clock.UtcNow.AddHours(-3),
            CreatedAtUtc = clock.UtcNow.AddHours(-3),
            UpdatedAtUtc = clock.UtcNow.AddHours(-3)
        };
        dbContext.EmailProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);

        var jobs = await queue.DequeuePendingBatchAsync(5);

        Assert.Empty(jobs);
        Assert.Equal(EmailProcessingStatus.Failed, job.Status);
        Assert.Null(job.LockedAtUtc);
        Assert.Equal("Timed out on its last attempt.", job.LastError);
    }

    [Fact]
    public async Task DequeuePendingBatchAsync_LeavesActiveProcessingJobLocked()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var email = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        var lockedAtUtc = clock.UtcNow.AddMinutes(-5);
        var job = new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = EmailProcessingStatus.Processing,
            Attempts = 1,
            AvailableAtUtc = clock.UtcNow.AddDays(-1),
            LockedAtUtc = lockedAtUtc,
            CreatedAtUtc = clock.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = clock.UtcNow.AddMinutes(-5)
        };
        dbContext.EmailProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);

        var jobs = await queue.DequeuePendingBatchAsync(5, "jordan.lee@example.edu");

        Assert.Empty(jobs);
        Assert.Equal(EmailProcessingStatus.Processing, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(lockedAtUtc, job.LockedAtUtc);
        Assert.Null(job.LastError);
    }

    [Fact]
    public async Task MarkFailedAsync_TagsFailureWithAnalysisVersion()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var email = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        var job = new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = EmailProcessingStatus.Processing,
            Attempts = 3,
            AvailableAtUtc = clock.UtcNow.AddDays(-1),
            LockedAtUtc = clock.UtcNow,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        dbContext.EmailProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);

        await queue.MarkFailedAsync(job.Id, "Parser failed.");

        Assert.Equal(EmailProcessingStatus.Failed, job.Status);
        Assert.Contains(EmailAnalysisVersion.Marker, job.LastError, StringComparison.Ordinal);
        Assert.Contains("Parser failed.", job.LastError, StringComparison.Ordinal);
        Assert.Equal("Exceeded max attempts (3).", job.DeadLetterReason);
        Assert.Equal(clock.UtcNow, job.DeadLetteredAtUtc);
    }

    [Fact]
    public async Task MarkFailedAsync_UsesConfiguredRetryPolicy()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EmailProcessing:MaxAttempts"] = "5",
                    ["EmailProcessing:RetryDelaySeconds"] = "120"
                })
            .Build();

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var email = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        var job = new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = EmailProcessingStatus.Processing,
            Attempts = 3,
            AvailableAtUtc = clock.UtcNow.AddDays(-1),
            LockedAtUtc = clock.UtcNow,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        dbContext.EmailProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock, configuration);

        await queue.MarkFailedAsync(job.Id, "Transient parser failure.");

        Assert.Equal(EmailProcessingStatus.Pending, job.Status);
        Assert.Equal(clock.UtcNow.AddSeconds(120), job.AvailableAtUtc);
        Assert.Null(job.DeadLetterReason);
        Assert.Null(job.DeadLetteredAtUtc);
    }

    [Fact]
    public async Task CountReadyAsync_ExcludesJobsAtConfiguredMaxAttempts()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EmailProcessing:MaxAttempts"] = "2"
                })
            .Build();

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var readyEmail = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        var overAttemptEmail = AddInboxEmail(dbContext, "sam.patel@example.edu", clock.UtcNow.AddMinutes(-20));
        dbContext.EmailProcessingJobs.AddRange(
            new EmailProcessingJob
            {
                InboxEmailId = readyEmail.Id,
                InboxEmail = readyEmail,
                Status = EmailProcessingStatus.Pending,
                Attempts = 1,
                AvailableAtUtc = clock.UtcNow.AddMinutes(-1)
            },
            new EmailProcessingJob
            {
                InboxEmailId = overAttemptEmail.Id,
                InboxEmail = overAttemptEmail,
                Status = EmailProcessingStatus.Pending,
                Attempts = 2,
                AvailableAtUtc = clock.UtcNow.AddMinutes(-1)
            });
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock, configuration);

        Assert.Equal(1, await queue.CountReadyAsync());
        var jobs = await queue.DequeuePendingBatchAsync(5);
        Assert.Single(jobs);
        Assert.Equal(readyEmail.Id, jobs[0].InboxEmailId);
    }

    [Fact]
    public async Task MarkCompletedAsync_IgnoresJob_WhenLeaseNoLongerMatches()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));
        var originalLock = clock.UtcNow.AddMinutes(-15);
        var currentLock = clock.UtcNow;

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var email = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        var job = new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = EmailProcessingStatus.Processing,
            Attempts = 1,
            AvailableAtUtc = clock.UtcNow.AddDays(-1),
            LockedAtUtc = originalLock,
            CreatedAtUtc = originalLock,
            UpdatedAtUtc = originalLock
        };
        dbContext.EmailProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var reclaimedJob = await dbContext.EmailProcessingJobs.FindAsync(job.Id);
        Assert.NotNull(reclaimedJob);
        reclaimedJob.LockedAtUtc = currentLock;
        reclaimedJob.UpdatedAtUtc = currentLock;
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);

        await queue.MarkCompletedAsync(job.Id, lockedAtUtc: originalLock);

        var storedJob = dbContext.EmailProcessingJobs.Single(candidate => candidate.Id == job.Id);
        Assert.Equal(EmailProcessingStatus.Processing, storedJob.Status);
        Assert.Equal(currentLock, storedJob.LockedAtUtc);
        Assert.Null(storedJob.CompletedAtUtc);
    }

    [Fact]
    public async Task MarkFailedAsync_IgnoresJob_WhenLeaseNoLongerMatches()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));
        var originalLock = clock.UtcNow.AddMinutes(-15);
        var currentLock = clock.UtcNow;

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var email = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        var job = new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = EmailProcessingStatus.Processing,
            Attempts = 1,
            AvailableAtUtc = clock.UtcNow.AddDays(-1),
            LockedAtUtc = originalLock,
            CreatedAtUtc = originalLock,
            UpdatedAtUtc = originalLock
        };
        dbContext.EmailProcessingJobs.Add(job);
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var reclaimedJob = await dbContext.EmailProcessingJobs.FindAsync(job.Id);
        Assert.NotNull(reclaimedJob);
        reclaimedJob.LockedAtUtc = currentLock;
        reclaimedJob.UpdatedAtUtc = currentLock;
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);

        await queue.MarkFailedAsync(job.Id, "Stale worker failure.", lockedAtUtc: originalLock);

        var storedJob = dbContext.EmailProcessingJobs.Single(candidate => candidate.Id == job.Id);
        Assert.Equal(EmailProcessingStatus.Processing, storedJob.Status);
        Assert.Equal(currentLock, storedJob.LockedAtUtc);
        Assert.Null(storedJob.LastError);
    }

    [Fact]
    public async Task EnqueueAsync_ReturnsZero_WhenJobAlreadyExists()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var email = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);

        var firstResult = await queue.EnqueueAsync(email.Id);
        var secondResult = await queue.EnqueueAsync(email.Id);

        Assert.Equal(1, firstResult);
        Assert.Equal(0, secondResult);
        Assert.Single(dbContext.EmailProcessingJobs);
    }

    [Fact]
    public async Task EnqueueAsync_CapturesCurrentCorrelationId()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var email = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        using var scope = CareerocTelemetry.BeginCorrelationScope("corr-queue", "sync-run");

        var result = await queue.EnqueueAsync(email.Id);

        var job = Assert.Single(dbContext.EmailProcessingJobs);
        Assert.Equal(1, result);
        Assert.Equal(email.Id.ToString("N"), job.RunId);
        Assert.Equal("corr-queue", job.CorrelationId);
    }

    [Fact]
    public async Task EnqueueAsync_ReturnsZero_WhenActiveQueueLimitIsReached()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 21, 0, 0, TimeSpan.Zero));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EmailProcessing:MaxActiveJobs"] = "1"
                })
            .Build();

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var firstEmail = AddInboxEmail(dbContext, "jordan.lee@example.edu", clock.UtcNow.AddMinutes(-30));
        var secondEmail = AddInboxEmail(dbContext, "sam.patel@example.edu", clock.UtcNow.AddMinutes(-20));
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock, configuration);

        var firstResult = await queue.EnqueueAsync(firstEmail.Id);
        var secondResult = await queue.EnqueueAsync(secondEmail.Id);

        Assert.Equal(1, firstResult);
        Assert.Equal(0, secondResult);
        Assert.Single(dbContext.EmailProcessingJobs);
    }

    private static InboxEmail AddInboxEmail(
        Careeroc.Infrastructure.Persistence.CareerocDbContext dbContext,
        string userEmail,
        DateTimeOffset receivedAtUtc)
    {
        var user = new User
        {
            Email = userEmail,
            Name = "Jordan Lee"
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
            ExternalMessageId = Guid.NewGuid().ToString("N"),
            ThreadId = Guid.NewGuid().ToString("N"),
            Sender = "recruiting@example.com",
            Subject = "Application update",
            RawBody = "Thanks for applying.",
            ParsedBody = "Thanks for applying.",
            ReceivedAtUtc = receivedAtUtc
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        return email;
    }
}
