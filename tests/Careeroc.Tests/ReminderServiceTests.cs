using Careeroc.Application.Services;
using Careeroc.Contracts.Reminders;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class ReminderServiceTests
{
    [Fact]
    public async Task UpdateStatusAsync_CompletesReminder_WithoutWritingNoiseEvent()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 4, 3, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };

        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Ramp",
            Role = "Software Engineer",
            Status = ApplicationStatus.OaPending,
            Priority = 5,
            LastActivityAtUtc = clock.UtcNow.AddDays(-1)
        };

        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.AssessmentDeadline,
            Title = "Complete assessment for Ramp",
            DueAtUtc = clock.UtcNow.AddDays(3),
            Status = ReminderStatus.Open
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        var service = new ReminderService(dbContext, clock);

        var updated = await service.UpdateStatusAsync(
            reminder.Id,
            new UpdateReminderStatusRequest
            {
                Status = "Completed"
            },
            user.Email);

        Assert.True(updated);
        Assert.Equal(ReminderStatus.Completed, reminder.Status);
        Assert.Equal(clock.UtcNow, application.LastActivityAtUtc);

        Assert.Empty(dbContext.JobApplicationEvents);
    }

    [Fact]
    public async Task UpdateStatusAsync_RejectsCrossUserReminderMutations()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 4, 4, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var owner = new User
        {
            Email = "owner@careeroc.dev",
            Name = "Owner"
        };

        var intruder = new User
        {
            Email = "intruder@careeroc.dev",
            Name = "Intruder"
        };

        var application = new JobApplication
        {
            UserId = owner.Id,
            User = owner,
            Company = "Notion",
            Role = "Product Engineer",
            Status = ApplicationStatus.Interviewing,
            Priority = 4,
            LastActivityAtUtc = clock.UtcNow.AddDays(-1)
        };

        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.FollowUp,
            Title = "Reply to Notion recruiter",
            DueAtUtc = clock.UtcNow.AddDays(2),
            Status = ReminderStatus.Open
        };

        dbContext.Users.AddRange(owner, intruder);
        dbContext.JobApplications.Add(application);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        var service = new ReminderService(dbContext, clock);

        var updated = await service.UpdateStatusAsync(
            reminder.Id,
            new UpdateReminderStatusRequest
            {
                Status = "Completed"
            },
            intruder.Email);

        Assert.False(updated);
        Assert.Equal(ReminderStatus.Open, reminder.Status);
        Assert.Equal(clock.UtcNow.AddDays(-1), application.LastActivityAtUtc);
        Assert.Empty(dbContext.JobApplicationEvents);
    }

    [Fact]
    public async Task SnoozeAsync_MovesDueDate_AndWritesApplicationEvent()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 4, 4, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };

        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Figma",
            Role = "Product Engineer",
            Status = ApplicationStatus.Offer,
            Priority = 5,
            LastActivityAtUtc = clock.UtcNow.AddDays(-1)
        };

        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.FollowUp,
            Title = "Reply to Figma offer",
            DueAtUtc = clock.UtcNow.AddHours(2),
            Status = ReminderStatus.Open
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        var service = new ReminderService(dbContext, clock);
        var snoozeUntil = clock.UtcNow.AddDays(1);

        var updated = await service.SnoozeAsync(
            reminder.Id,
            new SnoozeReminderRequest
            {
                Until = snoozeUntil
            },
            user.Email);

        Assert.True(updated);
        Assert.Equal(ReminderStatus.Snoozed, reminder.Status);
        Assert.Equal(snoozeUntil, reminder.DueAtUtc);
        Assert.Equal(clock.UtcNow, application.LastActivityAtUtc);

        var applicationEvent = Assert.Single(dbContext.JobApplicationEvents);
        Assert.Equal(application.Id, applicationEvent.JobApplicationId);
        Assert.Equal("reminder_snoozed", applicationEvent.EventType);
        Assert.Contains("Reply to Figma offer", applicationEvent.ExtractedDataJson);
    }
}
