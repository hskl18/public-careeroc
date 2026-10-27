using Careeroc.Application;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;
using Careeroc.Infrastructure.Persistence;

namespace Careeroc.Tests;

public sealed class ReminderEvidenceRefreshServiceTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 4, 24, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshPendingRemindersAsync_RehydratesFallbackDueDate_FromSourceEmail()
    {
        var clock = new TestClock(TestNow);
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var scenario = await SeedScenarioAsync(
            dbContext,
            clock,
            reminderType: ReminderType.FollowUp,
            applicationStatus: ApplicationStatus.RecruiterContact,
            reminderDueAtUtc: clock.UtcNow.AddDays(2),
            emails:
            [
                new EmailSeed(
                    Key: "veeva-rehydrate",
                    Subject: "We thought you could be a strong fit for our internship",
                    Body: "Hi Alex, we thought you could be a strong fit for the Software Engineering Internship at Veeva Systems. Please reply with your availability by tomorrow morning.",
                    Category: EmailCategory.RecruiterOutreach)
            ]);

        var result = await CreateService(dbContext, clock).RefreshPendingRemindersAsync();

        var expectedDueDateUtc = new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(1, result.RemindersRefreshed);
        Assert.Equal(1, result.EmailsUpdated);
        Assert.Equal(expectedDueDateUtc, scenario.Emails.Single().DueDateUtc);
        Assert.Equal(expectedDueDateUtc, scenario.Reminder.DueAtUtc);
        Assert.Equal(clock.UtcNow, scenario.Reminder.EvidenceRefreshedAtUtc);

        var secondRun = await CreateService(dbContext, clock).RefreshPendingRemindersAsync();

        Assert.Equal(0, secondRun.RemindersRefreshed);
        Assert.Equal(0, secondRun.EmailsUpdated);
    }

    [Fact]
    public async Task RefreshReminderAsync_ForcesRefresh_WhenNoNewEmailArrived()
    {
        var clock = new TestClock(TestNow);
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var scenario = await SeedScenarioAsync(
            dbContext,
            clock,
            evidenceRefreshedAtUtc: clock.UtcNow,
            emails:
            [
                new EmailSeed(
                    Key: "veeva-manual-refresh",
                    Subject: "Complete your assessment",
                    Body: "Please complete the assessment by April 29, 2026.",
                    Category: EmailCategory.OaInvitation,
                    ReceivedHoursAgo: 5,
                    ProcessedMinutesAgo: 240)
            ]);

        var backgroundRun = await CreateService(dbContext, clock).RefreshPendingRemindersAsync();

        Assert.Equal(0, backgroundRun.RemindersRefreshed);
        Assert.Equal(0, backgroundRun.EmailsUpdated);

        var manualRun = await CreateService(dbContext, clock).RefreshReminderAsync(
            scenario.Reminder.Id,
            scenario.User.Email);

        var expectedDueDateUtc = new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero);
        Assert.NotNull(manualRun);
        Assert.Equal(1, manualRun.RemindersRefreshed);
        Assert.Equal(1, manualRun.EmailsUpdated);
        Assert.Equal(expectedDueDateUtc, scenario.Emails.Single().DueDateUtc);
        Assert.Equal(expectedDueDateUtc, scenario.Reminder.DueAtUtc);
    }

    [Fact]
    public async Task RefreshReminderAsync_RehydratesAssessmentWindowDeadline()
    {
        var clock = new TestClock(TestNow);
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var scenario = await SeedScenarioAsync(
            dbContext,
            clock,
            company: "Carrier Global Corporation",
            role: "Software Engineering Intern",
            emails:
            [
                new EmailSeed(
                    Key: "carrier-window-deadline",
                    Subject: "Complete your assessment",
                    Body: "Please complete your assessment within 48 hours of receiving this invitation.",
                    Category: EmailCategory.OaInvitation,
                    ReceivedHoursAgo: 4,
                    ProcessedMinutesAgo: 120)
            ]);

        var result = await CreateService(dbContext, clock).RefreshReminderAsync(
            scenario.Reminder.Id,
            scenario.User.Email);

        var expectedDueDateUtc = clock.UtcNow.AddHours(-4).AddHours(48);
        Assert.NotNull(result);
        Assert.Equal(1, result.RemindersRefreshed);
        Assert.Equal(1, result.EmailsUpdated);
        Assert.Equal(expectedDueDateUtc, scenario.Emails.Single().DueDateUtc);
        Assert.Equal(expectedDueDateUtc, scenario.Reminder.DueAtUtc);
    }

    [Fact]
    public async Task RefreshReminderAsync_ReturnsNull_ForDifferentUser()
    {
        var clock = new TestClock(TestNow);
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var scenario = await SeedScenarioAsync(dbContext, clock, emails: []);

        var result = await CreateService(dbContext, clock).RefreshReminderAsync(
            scenario.Reminder.Id,
            "other@example.com");

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshPendingRemindersAsync_AlignsReminderWithStoredExplicitEvidence()
    {
        var clock = new TestClock(TestNow);
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var explicitDueDateUtc = new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero);
        var scenario = await SeedScenarioAsync(
            dbContext,
            clock,
            company: "Carrier Global Corporation",
            role: "Software Engineering Intern",
            emails:
            [
                new EmailSeed(
                    Key: "carrier-assessment",
                    Subject: "Complete your assessment",
                    Body: "Please complete the assessment by April 29, 2026.",
                    Category: EmailCategory.OaInvitation,
                    DueDateUtc: explicitDueDateUtc,
                    ReceivedHoursAgo: 4,
                    ProcessedMinutesAgo: 120)
            ]);

        var result = await CreateService(dbContext, clock).RefreshPendingRemindersAsync();

        Assert.Equal(1, result.RemindersRefreshed);
        Assert.Equal(0, result.EmailsUpdated);
        Assert.Equal(explicitDueDateUtc, scenario.Reminder.DueAtUtc);
        Assert.Equal(clock.UtcNow, scenario.Reminder.EvidenceRefreshedAtUtc);
    }

    [Fact]
    public async Task RefreshPendingRemindersAsync_ScansCandidateEvidenceBeforeSelectingDeadline()
    {
        var clock = new TestClock(TestNow);
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var scenario = await SeedScenarioAsync(
            dbContext,
            clock,
            company: "Carrier Global Corporation",
            role: "Software Engineering Intern",
            emails:
            [
                new EmailSeed(
                    Key: "carrier-status",
                    Subject: "Assessment status",
                    Body: "Thanks for starting your assessment. We will follow up soon.",
                    Category: EmailCategory.OaInvitation),
                new EmailSeed(
                    Key: "carrier-deadline",
                    Subject: "Complete your assessment",
                    Body: "Please complete the assessment by April 29, 2026.",
                    Category: EmailCategory.OaInvitation,
                    ReceivedHoursAgo: 5,
                    ProcessedMinutesAgo: 240)
            ]);

        var result = await CreateService(dbContext, clock).RefreshPendingRemindersAsync();

        var expectedDueDateUtc = new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(1, result.RemindersRefreshed);
        Assert.Equal(1, result.EmailsUpdated);
        Assert.Null(scenario.Emails.Single(email => email.ExternalMessageId == "carrier-status").DueDateUtc);
        Assert.Equal(expectedDueDateUtc, scenario.Emails.Single(email => email.ExternalMessageId == "carrier-deadline").DueDateUtc);
        Assert.Equal(expectedDueDateUtc, scenario.Reminder.DueAtUtc);
        Assert.Equal(clock.UtcNow, scenario.Reminder.EvidenceRefreshedAtUtc);
    }

    private static ReminderEvidenceRefreshService CreateService(
        CareerocDbContext dbContext,
        TestClock clock)
    {
        return new ReminderEvidenceRefreshService(
            dbContext,
            new HeuristicDueDateExtractor(new HeuristicRecruitingEmailParser()),
            new ApplicationWorkflowService(dbContext, clock),
            clock);
    }

    private static async Task<ReminderScenario> SeedScenarioAsync(
        CareerocDbContext dbContext,
        TestClock clock,
        IReadOnlyList<EmailSeed> emails,
        string company = "Veeva Systems",
        string role = "Software Engineering Internship",
        ApplicationStatus applicationStatus = ApplicationStatus.OaPending,
        ReminderType reminderType = ReminderType.AssessmentDeadline,
        DateTimeOffset? reminderDueAtUtc = null,
        DateTimeOffset? evidenceRefreshedAtUtc = null)
    {
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
            EmailAddress = "alex.personal@example.com"
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = company,
            Role = role,
            Source = "Email",
            Status = applicationStatus,
            Priority = 5,
            CreatedAtUtc = clock.UtcNow.AddDays(-3),
            LastActivityAtUtc = clock.UtcNow.AddHours(-2)
        };
        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = reminderType,
            Title = reminderType == ReminderType.FollowUp
                ? $"Reply to recruiter at {company}"
                : $"Complete assessment for {company}",
            DueAtUtc = reminderDueAtUtc ?? clock.UtcNow.AddDays(2),
            Status = ReminderStatus.Open,
            EvidenceRefreshedAtUtc = evidenceRefreshedAtUtc
        };
        var inboxEmails = emails
            .Select(seed => new InboxEmail
            {
                InboxAccountId = inboxAccount.Id,
                InboxAccount = inboxAccount,
                JobApplicationId = application.Id,
                JobApplication = application,
                ExternalMessageId = seed.Key,
                ThreadId = $"thread-{seed.Key}",
                Sender = "recruiting@example.com",
                Subject = seed.Subject,
                ParsedBody = seed.Body,
                RawBody = seed.Body,
                ReceivedAtUtc = clock.UtcNow.AddHours(-seed.ReceivedHoursAgo),
                DueDateUtc = seed.DueDateUtc,
                Category = seed.Category,
                ActionRequired = seed.ActionRequired,
                ProcessedAtUtc = clock.UtcNow.AddMinutes(-seed.ProcessedMinutesAgo)
            })
            .ToList();

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.AddRange(inboxEmails);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        return new ReminderScenario(user, reminder, inboxEmails);
    }

    private sealed record EmailSeed(
        string Key,
        string Subject,
        string Body,
        EmailCategory Category,
        bool ActionRequired = true,
        DateTimeOffset? DueDateUtc = null,
        int ReceivedHoursAgo = 1,
        int ProcessedMinutesAgo = 30);

    private sealed record ReminderScenario(
        User User,
        Reminder Reminder,
        IReadOnlyList<InboxEmail> Emails);
}
