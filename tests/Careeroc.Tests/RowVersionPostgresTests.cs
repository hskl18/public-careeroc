using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Tests;

// xmin row versions only exist in PostgreSQL; the in-memory provider never changes them.
public sealed class RowVersionPostgresTests
{
    [PostgresFact]
    public async Task ASyncThatReadTheInboxBeforeADisconnectCannotSaveOverIt()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid accountId;
        await using (var setup = database.CreateContext())
        {
            var user = new User { Email = "alex@example.com", Name = "Alex Rivera" };
            var account = new InboxAccount { User = user, Provider = "gmail", EmailAddress = "alex@gmail.com" };
            setup.AddRange(user, account);
            await setup.SaveChangesAsync();
            accountId = account.Id;
        }

        await using var sync = database.CreateContext();
        await using var person = database.CreateContext();
        var syncedCopy = await sync.InboxAccounts.SingleAsync(account => account.Id == accountId);
        var personsCopy = await person.InboxAccounts.SingleAsync(account => account.Id == accountId);

        personsCopy.OAuthMetadataJson = """{"connectionStatus":"Disconnected"}""";
        await person.SaveChangesAsync();
        syncedCopy.OAuthMetadataJson = """{"connectionStatus":"Connected"}""";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => sync.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task TwoRequestsThatBothReadAPendingProposalCannotBothResolveIt()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid proposalId;
        await using (var setup = database.CreateContext())
        {
            var user = new User { Email = "alex@example.com", Name = "Alex Rivera" };
            var proposal = new UserMemoryEntry
            {
                User = user,
                Kind = "preference",
                Key = "target_role",
                Value = "Backend engineer",
                SourceType = "user",
                ConfirmationStatus = "pending"
            };
            setup.AddRange(user, proposal);
            await setup.SaveChangesAsync();
            proposalId = proposal.Id;
        }

        // A double click: both requests read the proposal while it is pending, then both save.
        await using var firstClick = database.CreateContext();
        await using var secondClick = database.CreateContext();
        var first = await firstClick.UserMemoryEntries.SingleAsync(entry => entry.Id == proposalId);
        var second = await secondClick.UserMemoryEntries.SingleAsync(entry => entry.Id == proposalId);
        first.ConfirmationStatus = "confirmed";
        await firstClick.SaveChangesAsync();
        second.ConfirmationStatus = "confirmed";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondClick.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task ASecondApprovalAfterTheFirstFindsNothingToResolve()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var dbContext = database.CreateContext();
        var user = new User { Email = "alex@example.com", Name = "Alex Rivera" };
        var proposal = new UserMemoryEntry
        {
            User = user,
            Kind = "preference",
            Key = "target_role",
            Value = "Backend engineer",
            SourceType = "user",
            ConfirmationStatus = "pending"
        };
        dbContext.AddRange(user, proposal);
        await dbContext.SaveChangesAsync();
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero)));
        var request = new ResolveUserMemoryProposalRequest("confirm");

        Assert.NotNull(await service.ResolveProposalAsync(proposal.Id, request, user.Email));
        Assert.Null(await service.ResolveProposalAsync(proposal.Id, request, user.Email));
    }

    [PostgresFact]
    public async Task ASyncDropsItsResultsWhenThePersonDisconnectsTheInboxWhileItRuns()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var syncContext = database.CreateContext();
        var user = new User { Email = "alex@example.com", Name = "Alex Rivera" };
        var account = new InboxAccount { User = user, Provider = ApplicationDefaults.DemoInboxProvider, EmailAddress = "alex@demo.careeroc.example" };
        syncContext.AddRange(user, account);
        await syncContext.SaveChangesAsync();
        var clock = new TestClock(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        var source = new DisconnectingInboxSource(database, account.Id);
        var service = TestServices.EmailSyncService(
            syncContext,
            source,
            new DatabaseEmailProcessingQueue(syncContext, clock),
            clock,
            NullLogger<EmailSyncService>.Instance);

        var result = await service.RunOnceAsync((string?)null);

        Assert.Equal(EmailSyncStatuses.Skipped, result.Status);
        await using var check = database.CreateContext();
        var stored = await check.InboxAccounts.SingleAsync(candidate => candidate.Id == account.Id);
        Assert.Contains("Disconnected", stored.OAuthMetadataJson, StringComparison.Ordinal);
        Assert.Null(stored.LastSyncedAtUtc);
        Assert.Empty(check.InboxEmails);
    }

    // Returns one email, but first disconnects the inbox from another connection, as a person
    // clicking Disconnect during the Gmail call would.
    private sealed class DisconnectingInboxSource(PostgresTestDatabase database, Guid accountId) : IInboxEmailSource
    {
        public async Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
            InboxAccount inboxAccount,
            IReadOnlySet<string>? existingMessageIds = null,
            IReadOnlySet<string>? existingThreadIds = null,
            CancellationToken cancellationToken = default)
        {
            await using var person = database.CreateContext();
            var account = await person.InboxAccounts.SingleAsync(candidate => candidate.Id == accountId, cancellationToken);
            account.OAuthMetadataJson = """{"connectionStatus":"Disconnected"}""";
            await person.SaveChangesAsync(cancellationToken);

            return
            [
                new ExternalInboxEmail(
                    "message-1",
                    "thread-1",
                    "Recruiter <recruiting@kestrel.example>",
                    "Interview",
                    "Let's talk.",
                    new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero))
            ];
        }
    }
}

