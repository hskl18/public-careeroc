using Careeroc.Application;
using Careeroc.Application.Services;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Entities;

namespace Careeroc.Tests;

public sealed class UserMemoryProposalServiceTests
{
    [Fact]
    public async Task ProposeMemoryAsync_CreatesPendingProposalWithoutEnteringAgentContext()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var proposal = await service.ProposeMemoryAsync(
            new ProposeUserMemoryRequest(
                "preference",
                "target_stack",
                "full-stack TypeScript",
                "Inferred from resume and application history.",
                "resume_agent",
                Guid.NewGuid(),
                0.72,
                null),
            ApplicationDefaults.DefaultUserEmail);

        Assert.Equal("pending", proposal.ConfirmationStatus);
        Assert.Equal("preference", proposal.Kind);
        Assert.Equal("target_stack", proposal.Key);
        var pending = Assert.Single(await service.GetPendingProposalsAsync(ApplicationDefaults.DefaultUserEmail));
        Assert.Equal(proposal.Id, pending.Id);

        var snapshot = await new UserAgentContextSnapshotService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)))
            .GetSnapshotAsync(ApplicationDefaults.DefaultUserEmail);
        Assert.Equal(0, snapshot.MemoryEntryCount);
        Assert.DoesNotContain("target_stack", snapshot.MemoryMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposeMemoryAsync_UpdatesExistingPendingProposal()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var sourceId = Guid.NewGuid();

        var first = await service.ProposeMemoryAsync(
            new ProposeUserMemoryRequest(
                "preference",
                "target_stack",
                "React",
                null,
                "mailbox_agent",
                sourceId,
                0.5,
                null),
            ApplicationDefaults.DefaultUserEmail);
        var second = await service.ProposeMemoryAsync(
            new ProposeUserMemoryRequest(
                "preference",
                "target_stack",
                "React and C#",
                "Updated with more evidence.",
                "mailbox_agent",
                sourceId,
                0.8,
                null),
            ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("React and C#", second.Value);
        Assert.Equal(0.8, second.Confidence);
        Assert.Single(dbContext.UserMemoryEntries);
    }

    [Fact]
    public async Task ProposeMemoryAsync_CreatesPendingUpdateProposalForConfirmedMemory()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        dbContext.Users.Add(user);
        dbContext.UserMemoryEntries.Add(
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "preference",
                Key = "target_stack",
                Value = "backend C#",
                SourceType = "user",
                ConfirmationStatus = "confirmed"
            });
        await dbContext.SaveChangesAsync();
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var proposal = await service.ProposeMemoryAsync(
            new ProposeUserMemoryRequest(
                "preference",
                "target_stack",
                "frontend React",
                "Agent found a newer stack signal.",
                "resume_agent",
                null,
                0.9,
                null),
            ApplicationDefaults.DefaultUserEmail);

        Assert.Equal("pending", proposal.ConfirmationStatus);
        Assert.Equal("frontend React", proposal.Value);
        Assert.Equal(2, dbContext.UserMemoryEntries.Count());
        Assert.Equal(
            "backend C#",
            dbContext.UserMemoryEntries.Single(entry => entry.ConfirmationStatus == "confirmed").Value);
    }

    [Fact]
    public async Task ResolveProposalAsync_ConfirmPromotesProposalIntoAgentContext()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var proposal = await service.ProposeMemoryAsync(
            new ProposeUserMemoryRequest(
                "preference",
                "target_stack",
                "full-stack TypeScript",
                "Strong repeated signal.",
                "orchestrator",
                null,
                0.84,
                null),
            ApplicationDefaults.DefaultUserEmail);

        var resolved = await service.ResolveProposalAsync(
            proposal.Id,
            new ResolveUserMemoryProposalRequest("confirm"),
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(resolved);
        Assert.Equal("confirmed", resolved.ConfirmationStatus);
        Assert.Empty(await service.GetPendingProposalsAsync(ApplicationDefaults.DefaultUserEmail));
        var snapshot = await new UserAgentContextSnapshotService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)))
            .GetSnapshotAsync(ApplicationDefaults.DefaultUserEmail);
        Assert.Equal(1, snapshot.MemoryEntryCount);
        Assert.Contains("target_stack", snapshot.MemoryMarkdown, StringComparison.Ordinal);
        Assert.Contains("full-stack TypeScript", snapshot.MemoryMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveProposalAsync_RejectRemovesProposalFromPendingAndContext()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var proposal = await service.ProposeMemoryAsync(
            new ProposeUserMemoryRequest(
                "preference",
                "target_stack",
                "frontend React",
                null,
                "resume_agent",
                null,
                0.64,
                null),
            ApplicationDefaults.DefaultUserEmail);

        var resolved = await service.ResolveProposalAsync(
            proposal.Id,
            new ResolveUserMemoryProposalRequest("reject"),
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(resolved);
        Assert.Equal("rejected", resolved.ConfirmationStatus);
        Assert.Empty(await service.GetPendingProposalsAsync(ApplicationDefaults.DefaultUserEmail));
        var snapshot = await new UserAgentContextSnapshotService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)))
            .GetSnapshotAsync(ApplicationDefaults.DefaultUserEmail);
        Assert.Equal(0, snapshot.MemoryEntryCount);
        Assert.DoesNotContain("frontend React", snapshot.MemoryMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveProposalAsync_DoesNotCrossUserBoundary()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var proposal = await service.ProposeMemoryAsync(
            new ProposeUserMemoryRequest(
                "preference",
                "target_stack",
                "full-stack TypeScript",
                null,
                "resume_agent",
                null,
                0.64,
                null),
            "owner@example.com");

        var resolved = await service.ResolveProposalAsync(
            proposal.Id,
            new ResolveUserMemoryProposalRequest("confirm"),
            "other@example.com");

        Assert.Null(resolved);
        Assert.Equal("pending", dbContext.UserMemoryEntries.Single(entry => entry.Id == proposal.Id).ConfirmationStatus);
    }

    [Fact]
    public async Task ResolveProposalAsync_ConfirmAppliesUpdateToExistingConfirmedMemory()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        dbContext.Users.Add(user);
        dbContext.UserMemoryEntries.Add(
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "preference",
                Key = "target_stack",
                Value = "backend C#",
                SourceType = "user",
                ConfirmationStatus = "confirmed"
            });
        dbContext.UserMemoryEntries.Add(
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "preference",
                Key = "target_stack",
                Value = "frontend React",
                SourceType = "resume_agent",
                ConfirmationStatus = "pending"
            });
        await dbContext.SaveChangesAsync();
        var proposal = dbContext.UserMemoryEntries.Single(entry => entry.ConfirmationStatus == "pending");
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var resolved = await service.ResolveProposalAsync(
            proposal.Id,
            new ResolveUserMemoryProposalRequest("confirm"),
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(resolved);
        Assert.Equal("confirmed", resolved.ConfirmationStatus);
        Assert.Equal("frontend React", resolved.Value);
        Assert.Empty(await service.GetPendingProposalsAsync(ApplicationDefaults.DefaultUserEmail));
        Assert.Equal("applied", dbContext.UserMemoryEntries.Single(entry => entry.Id == proposal.Id).ConfirmationStatus);
        var confirmed = dbContext.UserMemoryEntries.Single(entry => entry.ConfirmationStatus == "confirmed");
        Assert.Equal("frontend React", confirmed.Value);
        Assert.Equal("resume_agent", confirmed.SourceType);
    }

    [Fact]
    public async Task GetConfirmedMemoryAsync_ReturnsConfirmedActiveMemoryOnly()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        dbContext.Users.Add(user);
        var nowUtc = new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);
        dbContext.UserMemoryEntries.AddRange(
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "preference",
                Key = "target_stack",
                Value = "backend C#",
                SourceType = "user",
                ConfirmationStatus = "confirmed"
            },
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "preference",
                Key = "pending_stack",
                Value = "React",
                SourceType = "agent",
                ConfirmationStatus = "pending"
            },
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "preference",
                Key = "expired_stack",
                Value = "Ruby",
                SourceType = "user",
                ConfirmationStatus = "confirmed",
                ExpiresAtUtc = nowUtc.AddDays(-1)
            });
        await dbContext.SaveChangesAsync();
        var service = new UserMemoryProposalService(dbContext, new TestClock(nowUtc));

        var memory = await service.GetConfirmedMemoryAsync(ApplicationDefaults.DefaultUserEmail);

        var entry = Assert.Single(memory);
        Assert.Equal("target_stack", entry.Key);
    }

    [Fact]
    public async Task ReadMemoryAsync_DoesNotCreateUser_WhenUserIsMissing()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new UserMemoryProposalService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var pending = await service.GetPendingProposalsAsync("missing@example.com");
        var confirmed = await service.GetConfirmedMemoryAsync("missing@example.com");

        Assert.Empty(pending);
        Assert.Empty(confirmed);
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task UpdateConfirmedMemoryAsync_UpdatesMemoryAndAgentContext()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var memory = new UserMemoryEntry
        {
            User = user,
            UserId = user.Id,
            Kind = "preference",
            Key = "target_stack",
            Value = "backend C#",
            Summary = "Original",
            SourceType = "resume_agent",
            Confidence = 0.7,
            ConfirmationStatus = "confirmed"
        };
        dbContext.Users.Add(user);
        dbContext.UserMemoryEntries.Add(memory);
        await dbContext.SaveChangesAsync();
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var updated = await service.UpdateConfirmedMemoryAsync(
            memory.Id,
            new UpdateUserMemoryRequest(
                "backend C# and TypeScript",
                "User corrected stack preference.",
                0.98,
                null),
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(updated);
        Assert.Equal("backend C# and TypeScript", updated.Value);
        Assert.Equal("User corrected stack preference.", updated.Summary);
        Assert.Equal(0.98, updated.Confidence);
        Assert.Equal("user", dbContext.UserMemoryEntries.Single().SourceType);
        var snapshot = await new UserAgentContextSnapshotService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)))
            .GetSnapshotAsync(ApplicationDefaults.DefaultUserEmail);
        Assert.Contains("backend C# and TypeScript", snapshot.MemoryMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateConfirmedMemoryAsync_NormalizesStructuredJsonMemory()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var memory = new UserMemoryEntry
        {
            User = user,
            UserId = user.Id,
            Kind = "resume",
            Key = "latest_evaluation",
            Value = "{}",
            SourceType = "resume_agent",
            Confidence = 0.7,
            ConfirmationStatus = "confirmed"
        };
        dbContext.Users.Add(user);
        dbContext.UserMemoryEntries.Add(memory);
        await dbContext.SaveChangesAsync();
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var updated = await service.UpdateConfirmedMemoryAsync(
            memory.Id,
            new UpdateUserMemoryRequest(
                """
                {
                  "targetRole": "Machine Learning Engineer",
                  "skills": ["C#", "TypeScript"]
                }
                """,
                "Structured resume memory.",
                0.95,
                null),
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(updated);
        Assert.Equal("{\"targetRole\":\"Machine Learning Engineer\",\"skills\":[\"C#\",\"TypeScript\"]}", updated.Value);
    }

    [Fact]
    public async Task UpdateConfirmedMemoryAsync_RejectsInvalidStructuredJsonMemory()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var memory = new UserMemoryEntry
        {
            User = user,
            UserId = user.Id,
            Kind = "resume",
            Key = "latest_evaluation",
            Value = "{}",
            SourceType = "resume_agent",
            Confidence = 0.7,
            ConfirmationStatus = "confirmed"
        };
        dbContext.Users.Add(user);
        dbContext.UserMemoryEntries.Add(memory);
        await dbContext.SaveChangesAsync();
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateConfirmedMemoryAsync(
                memory.Id,
                new UpdateUserMemoryRequest(
                    "{ bad json",
                    "Broken structured memory.",
                    0.95,
                    null),
                ApplicationDefaults.DefaultUserEmail));

        Assert.Equal("Memory value looks like JSON but is not valid JSON.", exception.Message);
        Assert.Equal("{}", dbContext.UserMemoryEntries.Single().Value);
    }

    [Fact]
    public async Task UpdateConfirmedMemoryAsync_DoesNotCrossUserBoundaryOrUpdatePending()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var ownerProposal = await service.ProposeMemoryAsync(
            new ProposeUserMemoryRequest(
                "preference",
                "target_stack",
                "React",
                null,
                "agent",
                null,
                0.6,
                null),
            "owner@example.com");

        var pendingUpdate = await service.UpdateConfirmedMemoryAsync(
            ownerProposal.Id,
            new UpdateUserMemoryRequest("Vue", null, null, null),
            "owner@example.com");
        var crossUserUpdate = await service.UpdateConfirmedMemoryAsync(
            ownerProposal.Id,
            new UpdateUserMemoryRequest("Vue", null, null, null),
            "other@example.com");

        Assert.Null(pendingUpdate);
        Assert.Null(crossUserUpdate);
        Assert.Equal("React", dbContext.UserMemoryEntries.Single(entry => entry.Id == ownerProposal.Id).Value);
    }

    [Fact]
    public async Task DeleteMemoryAsync_RemovesMemoryFromAgentContextAndRespectsUserBoundary()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = "owner@example.com",
            Name = "Owner"
        };
        var memory = new UserMemoryEntry
        {
            User = user,
            UserId = user.Id,
            Kind = "preference",
            Key = "target_stack",
            Value = "backend C#",
            SourceType = "user",
            ConfirmationStatus = "confirmed"
        };
        dbContext.Users.Add(user);
        dbContext.UserMemoryEntries.Add(memory);
        await dbContext.SaveChangesAsync();
        var service = new UserMemoryProposalService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(await service.DeleteMemoryAsync(memory.Id, "other@example.com"));
        Assert.True(await service.DeleteMemoryAsync(memory.Id, "owner@example.com"));
        Assert.Empty(dbContext.UserMemoryEntries);
        var snapshot = await new UserAgentContextSnapshotService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)))
            .GetSnapshotAsync("owner@example.com");
        Assert.Equal(0, snapshot.MemoryEntryCount);
    }
}
