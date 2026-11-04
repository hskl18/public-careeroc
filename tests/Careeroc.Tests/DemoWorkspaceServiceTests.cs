using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Demo;
using Careeroc.Infrastructure.Email;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Careeroc.Tests;

public sealed class DemoWorkspaceServiceTests
{
    private const string VisitorEmail = "visitor-0001@demo.careeroc.example";

    [Fact]
    public async Task EnsureWorkspaceAsync_BuildsPipelineFromFictionalInbox()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var service = CreateService(dbContext, clock);

        var result = await service.EnsureWorkspaceAsync(VisitorEmail);

        Assert.True(result.Created);
        Assert.Equal(DemoInboxEmailSource.FixtureEmailCount, result.EmailsImported);
        var applications = await dbContext.JobApplications.AsNoTracking().ToListAsync();
        Assert.Contains(applications, application => application.Company == "Kestrel AI" && application.Status == ApplicationStatus.Offer);
        Assert.Contains(applications, application => application.Company == "Harborline" && application.Status == ApplicationStatus.Rejected);
        Assert.Contains(applications, application => application.Company == "Cobalt Pay" && application.Status == ApplicationStatus.OaPending);
        Assert.Contains(applications, application => application.Company == "Northwind Labs" && application.Status == ApplicationStatus.Interviewing);
        Assert.Contains(applications, application => application.Company == "Brightwave" && application.Status == ApplicationStatus.Applied);
        Assert.DoesNotContain(applications, application => application.Company.Contains("Jobs Digest", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(await dbContext.Reminders.AsNoTracking().ToListAsync());
        Assert.Single(await dbContext.ResumeDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task EnsureWorkspaceAsync_IsIdempotentForAnExistingWorkspace()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var service = CreateService(dbContext, clock);
        await service.EnsureWorkspaceAsync(VisitorEmail);
        var emailCount = await dbContext.InboxEmails.CountAsync();

        var second = await service.EnsureWorkspaceAsync(VisitorEmail);

        Assert.False(second.Created);
        Assert.Equal(emailCount, await dbContext.InboxEmails.CountAsync());
    }

    [Fact]
    public async Task EnsureWorkspaceAsync_RejectsIdentitiesOutsideTheDemoDomain()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var service = CreateService(dbContext, clock);

        await Assert.ThrowsAsync<DemoWorkspaceUnavailableException>(
            () => service.EnsureWorkspaceAsync("someone@example.com"));
        Assert.Empty(await dbContext.Users.ToListAsync());
    }

    [Fact]
    public async Task EnsureWorkspaceAsync_StopsAtTheHourlyCapacity()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        dbContext.Users.Add(new User { Email = "visitor-earlier@demo.careeroc.example", Name = "Earlier", CreatedAtUtc = clock.UtcNow.AddMinutes(-10) });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, clock, new DemoOptions { Enabled = true, MaxWorkspacesPerHour = 1 });

        await Assert.ThrowsAsync<DemoWorkspaceCapacityException>(() => service.EnsureWorkspaceAsync(VisitorEmail));
    }

    [PostgresFact]
    public async Task DeleteExpiredWorkspacesAsync_RemovesOnlyExpiredDemoUsersWithTheirData()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero));
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var setupContext = database.CreateContext())
        {
            await CreateService(setupContext, new TestClock(clock.UtcNow.AddHours(-30))).EnsureWorkspaceAsync(VisitorEmail);
            setupContext.Users.Add(new User { Email = "fresh@demo.careeroc.example", Name = "Fresh", CreatedAtUtc = clock.UtcNow.AddHours(-1) });
            setupContext.Users.Add(new User { Email = "member@example.com", Name = "Member", CreatedAtUtc = clock.UtcNow.AddDays(-90) });
            await setupContext.SaveChangesAsync();
        }

        await using var dbContext = database.CreateContext();
        var deleted = await CreateService(dbContext, clock).DeleteExpiredWorkspacesAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(
            ["fresh@demo.careeroc.example", "member@example.com"],
            await dbContext.Users.OrderBy(user => user.Email).Select(user => user.Email).ToListAsync());
        Assert.Empty(await dbContext.InboxEmails.ToListAsync());
        Assert.Empty(await dbContext.JobApplications.ToListAsync());
        Assert.Empty(await dbContext.ResumeDocuments.ToListAsync());
    }

    private static DemoWorkspaceService CreateService(
        CareerocDbContext dbContext,
        TestClock clock,
        DemoOptions? options = null)
    {
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        var intelligenceService = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            new ApplicationWorkflowService(dbContext, clock),
            clock,
            NullLogger<EmailIntelligenceService>.Instance);
        var syncService = new EmailSyncService(
            dbContext,
            new DemoInboxEmailSource(clock),
            queue,
            clock,
            NullLogger<EmailSyncService>.Instance);

        return new DemoWorkspaceService(
            dbContext,
            syncService,
            intelligenceService,
            new ResumeIntelligenceService(dbContext, clock),
            Options.Create(options ?? new DemoOptions { Enabled = true }),
            clock,
            NullLogger<DemoWorkspaceService>.Instance);
    }
}
