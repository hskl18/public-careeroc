using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class ApplicationWorkflowServiceTests
{
    [Theory]
    [InlineData("AI Engineer", "ML / AI")]
    [InlineData("Machine Learning Intern", "ML / AI")]
    [InlineData("ML Platform Engineer", "ML / AI")]
    [InlineData("Maintenance Technician", "Other / Unknown")]
    [InlineData("Email Marketing Specialist", "Other / Unknown")]
    [InlineData("SWE Intern", "Software Engineering")]
    [InlineData("Frontend Engineer", "Software Engineering / Frontend")]
    public void ResolveRoleCanonical_MatchesShortTokensAsWholeWords(string role, string expected)
    {
        Assert.Equal(expected, ApplicationWorkflowService.ResolveRoleCanonical(role));
    }

    [Fact]
    public void ApplyStatusAndPriority_ReopensRejectedApplication_ForNewerActiveWorkflowEvidence()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 20, 0, 0, TimeSpan.Zero));
        using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var service = new ApplicationWorkflowService(dbContext, clock);
        var application = new JobApplication
        {
            Company = "TikTok",
            Role = "Machine Learning Engineer Intern",
            Source = "Email",
            Status = ApplicationStatus.Rejected,
            Priority = 1,
            CreatedAtUtc = clock.UtcNow.AddDays(-30),
            LastActivityAtUtc = clock.UtcNow.AddDays(-7)
        };

        var resolvedStatus = service.ApplyStatusAndPriority(
            application,
            EmailCategory.InterviewConfirmation,
            ApplicationStatus.Interviewing,
            clock.UtcNow);

        Assert.Equal(ApplicationStatus.Interviewing, resolvedStatus);
        Assert.Equal(ApplicationStatus.Interviewing, application.Status);
        Assert.Equal(5, application.Priority);
        Assert.Equal(clock.UtcNow, application.LastActivityAtUtc);
    }

    [Fact]
    public void ApplyStatusAndPriority_KeepsRejectedApplication_ForOlderActiveWorkflowEvidence()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 20, 0, 0, TimeSpan.Zero));
        using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var service = new ApplicationWorkflowService(dbContext, clock);
        var application = new JobApplication
        {
            Company = "TikTok",
            Role = "Machine Learning Engineer Intern",
            Source = "Email",
            Status = ApplicationStatus.Rejected,
            Priority = 1,
            CreatedAtUtc = clock.UtcNow.AddDays(-30),
            LastActivityAtUtc = clock.UtcNow
        };

        var resolvedStatus = service.ApplyStatusAndPriority(
            application,
            EmailCategory.InterviewConfirmation,
            ApplicationStatus.Interviewing,
            clock.UtcNow.AddDays(-2));

        Assert.Equal(ApplicationStatus.Rejected, resolvedStatus);
        Assert.Equal(ApplicationStatus.Rejected, application.Status);
    }

    [Fact]
    public async Task MatchOrCreateApplicationAsync_MatchesCompanyLegalSuffixVariants()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 20, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Bio-Rad Laboratories, Inc.",
            Role = "Test Engineering Intern (Software)",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = clock.UtcNow.AddDays(-10),
            LastActivityAtUtc = clock.UtcNow.AddDays(-3)
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        await dbContext.SaveChangesAsync();

        var service = new ApplicationWorkflowService(dbContext, clock);

        var match = await service.MatchOrCreateApplicationAsync(
            user.Id,
            null,
            "Bio-Rad Laboratories",
            "Test Engineering Intern (Software)",
            ApplicationStatus.Rejected,
            EmailCategory.Rejection,
            clock.UtcNow,
            allowAutoCreate: true);

        Assert.False(match.Created);
        Assert.Equal(application.Id, match.Application?.Id);
        Assert.Equal(0.95m, match.MatchingConfidence);
        Assert.Single(dbContext.JobApplications);
    }

    [Theory]
    [InlineData("This Time")]
    [InlineData("At This Time")]
    [InlineData("This Company")]
    public async Task MatchOrCreateApplicationAsync_RejectsNonCompanyPhrases(string company)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new ApplicationWorkflowService(dbContext, clock);

        var match = await service.MatchOrCreateApplicationAsync(
            user.Id,
            null,
            company,
            "AI/ML Engineer",
            ApplicationStatus.Rejected,
            EmailCategory.Rejection,
            clock.UtcNow,
            allowAutoCreate: true);

        Assert.False(match.Created);
        Assert.Null(match.Application);
        Assert.Equal(0.35m, match.MatchingConfidence);
        Assert.Empty(dbContext.JobApplications);
        Assert.False(ApplicationWorkflowService.CanCreateApplication(company, "AI/ML Engineer"));
    }

    [Fact]
    public async Task EnsureReminderAsync_RefreshesOpenReminder_WhenNewEvidenceIsSooner()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var service = new ApplicationWorkflowService(dbContext, clock);
        var application = new JobApplication
        {
            Company = "HDSI Lab 3.0",
            Role = "Fellow",
            Source = "Email",
            Status = ApplicationStatus.RecruiterContact,
            Priority = 4,
            CreatedAtUtc = clock.UtcNow.AddDays(-10),
            LastActivityAtUtc = clock.UtcNow.AddDays(-1)
        };
        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.FollowUp,
            Title = "Reply to recruiter at HDSI Lab 3.0",
            DueAtUtc = new DateTimeOffset(2026, 10, 12, 23, 59, 0, TimeSpan.Zero),
            Status = ReminderStatus.Open
        };

        dbContext.JobApplications.Add(application);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        var created = await service.EnsureReminderAsync(
            application,
            new FollowUpTaskPlan(
                true,
                ReminderType.FollowUp,
                "Reply to recruiter at HDSI Lab 3.0",
                new DateTimeOffset(2026, 4, 24, 16, 0, 0, TimeSpan.Zero),
                "Newer recruiter email supplied a closer due date.",
                "Test"),
            CancellationToken.None);

        Assert.False(created);
        Assert.Single(dbContext.Reminders);
        Assert.Equal(new DateTimeOffset(2026, 4, 24, 16, 0, 0, TimeSpan.Zero), reminder.DueAtUtc);
        Assert.Equal("Reply to recruiter at HDSI Lab 3.0", reminder.Title);
    }

    [Fact]
    public async Task EnsureReminderAsync_RefreshesStalePastReminder_WhenNewEvidenceIsActionable()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var service = new ApplicationWorkflowService(dbContext, clock);
        var application = new JobApplication
        {
            Company = "AES",
            Role = "Summer Intern",
            Source = "Email",
            Status = ApplicationStatus.OaPending,
            Priority = 5,
            CreatedAtUtc = clock.UtcNow.AddDays(-30),
            LastActivityAtUtc = clock.UtcNow.AddDays(-1)
        };
        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.AssessmentDeadline,
            Title = "Complete assessment for AES",
            DueAtUtc = new DateTimeOffset(2026, 4, 4, 15, 1, 0, TimeSpan.Zero),
            Status = ReminderStatus.Open
        };

        dbContext.JobApplications.Add(application);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        var created = await service.EnsureReminderAsync(
            application,
            new FollowUpTaskPlan(
                true,
                ReminderType.AssessmentDeadline,
                "Complete assessment for AES",
                new DateTimeOffset(2026, 4, 25, 3, 0, 0, TimeSpan.Zero),
                "Reminder refreshed from a newer assessment email.",
                "Test"),
            CancellationToken.None);

        Assert.False(created);
        Assert.Single(dbContext.Reminders);
        Assert.Equal(new DateTimeOffset(2026, 4, 25, 3, 0, 0, TimeSpan.Zero), reminder.DueAtUtc);
    }
}
