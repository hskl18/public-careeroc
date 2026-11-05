using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Careeroc.Tests;

// The claim is raw SQL that only Postgres can run, so the in-memory provider cannot cover it.
public sealed class DatabaseEmailProcessingQueuePostgresTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedEmailAsync(PostgresTestDatabase database, string externalMessageId)
    {
        await using var dbContext = database.CreateContext();
        var user = new User { Email = $"{externalMessageId}@example.com", Name = "Queue" };
        var account = new InboxAccount { User = user, UserId = user.Id, Provider = "gmail", EmailAddress = user.Email };
        var email = new InboxEmail
        {
            InboxAccount = account,
            InboxAccountId = account.Id,
            ExternalMessageId = externalMessageId,
            ThreadId = externalMessageId,
            Sender = "recruiter@example.com",
            Subject = "Hello",
            ParsedBody = "Hello",
            RawBody = "Hello",
            ReceivedAtUtc = Now
        };
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();
        return email.Id;
    }

    [PostgresFact]
    public async Task DequeuePendingBatchAsync_ReturnsFreshValuesForJobsThisContextAlreadyTracks()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var emailId = await SeedEmailAsync(database, "tracked");
        await using var dbContext = database.CreateContext();
        var queue = new DatabaseEmailProcessingQueue(dbContext, new TestClock(Now));
        await queue.EnqueueAsync(emailId);

        var job = Assert.Single(await queue.DequeuePendingBatchAsync(10));

        Assert.Equal(EmailProcessingStatus.Processing, job.Status);
        Assert.NotNull(job.LockedAtUtc);
        Assert.Equal(1, job.Attempts);
    }

    [PostgresFact]
    public async Task DequeuePendingBatchAsync_ClaimsAJobWhoseOwnerRowIsLockedElsewhere()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var emailId = await SeedEmailAsync(database, "owner-locked");
        await using (var setup = database.CreateContext())
        {
            await new DatabaseEmailProcessingQueue(setup, new TestClock(Now)).EnqueueAsync(emailId);
        }

        // Updating a user in another transaction must not hide that user's jobs from the workers.
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var lockOwner = new NpgsqlCommand("SELECT id FROM users FOR UPDATE", connection, transaction))
        {
            await lockOwner.ExecuteNonQueryAsync();
        }

        await using var dbContext = database.CreateContext();
        var claimed = await new DatabaseEmailProcessingQueue(dbContext, new TestClock(Now)).DequeuePendingBatchAsync(10);

        Assert.Single(claimed);
        await transaction.RollbackAsync();
    }

    [PostgresFact]
    public async Task DequeuePendingBatchAsync_NeverGivesOneJobToTwoWorkers()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        for (var index = 0; index < 20; index++)
        {
            var emailId = await SeedEmailAsync(database, $"parallel-{index}");
            await using var setup = database.CreateContext();
            await new DatabaseEmailProcessingQueue(setup, new TestClock(Now)).EnqueueAsync(emailId);
        }

        async Task<List<Guid>> ClaimAsync()
        {
            await using var dbContext = database.CreateContext();
            var jobs = await new DatabaseEmailProcessingQueue(dbContext, new TestClock(Now)).DequeuePendingBatchAsync(10);
            return jobs.Select(job => job.Id).ToList();
        }

        var batches = await Task.WhenAll(ClaimAsync(), ClaimAsync(), ClaimAsync());
        var claimed = batches.SelectMany(batch => batch).ToList();

        Assert.Equal(claimed.Count, claimed.Distinct().Count());
        await using var verify = database.CreateContext();
        Assert.Equal(claimed.Count, await verify.EmailProcessingJobs.CountAsync(job => job.Status == EmailProcessingStatus.Processing));
    }
}
