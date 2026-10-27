using Careeroc.Application;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class DashboardQueryServiceTests
{
    private static readonly TestClock DashboardClock = new(new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetDashboardAsync_ExcludesIgnoredNoise_FromInboxOverviewCounts()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        var gmailInbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "alex.personal@example.com",
            LastSyncedAtUtc = new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.InboxEmails.AddRange(
            new InboxEmail
            {
                InboxAccountId = gmailInbox.Id,
                InboxAccount = gmailInbox,
                ExternalMessageId = "gmail-recruiting-1",
                ThreadId = "gmail-thread-recruiting-1",
                Sender = "careers@example.com",
                Subject = "Interview invitation",
                RawBody = "Please schedule your interview.",
                ParsedBody = "Please schedule your interview.",
                ReceivedAtUtc = new DateTimeOffset(2026, 4, 6, 9, 0, 0, TimeSpan.Zero),
                Category = EmailCategory.InterviewInvitation,
                ProcessedAtUtc = new DateTimeOffset(2026, 4, 6, 9, 5, 0, TimeSpan.Zero)
            },
            new InboxEmail
            {
                InboxAccountId = gmailInbox.Id,
                InboxAccount = gmailInbox,
                ExternalMessageId = "gmail-noise-1",
                ThreadId = "gmail-thread-noise-1",
                Sender = "noreply@robinhood.com",
                Subject = "Your account statement is available",
                RawBody = "View your statement.",
                ParsedBody = "View your statement.",
                ReceivedAtUtc = new DateTimeOffset(2026, 4, 6, 8, 0, 0, TimeSpan.Zero),
                ProcessedAtUtc = new DateTimeOffset(2026, 4, 6, 8, 5, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(1, response.Inbox.SyncedEmails);
        Assert.Equal(1, response.Inbox.ProcessedEmails);
    }

    [Fact]
    public async Task GetDashboardAsync_IncludesReviewQueueCount_ForVisibleManualReviewEmails()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        var gmailInbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "alex.personal@example.com"
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.InboxEmails.Add(
            new InboxEmail
            {
                InboxAccountId = gmailInbox.Id,
                InboxAccount = gmailInbox,
                ExternalMessageId = "gmail-review-1",
                ThreadId = "gmail-thread-review-1",
                Sender = "recruiting@example.com",
                Subject = "Interview follow-up",
                RawBody = "Please confirm your availability.",
                ParsedBody = "Please confirm your availability.",
                ReceivedAtUtc = new DateTimeOffset(2026, 4, 6, 8, 0, 0, TimeSpan.Zero),
                RequiresManualReview = true,
                ProcessedAtUtc = new DateTimeOffset(2026, 4, 6, 8, 5, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(1, response.Metrics.ReviewQueue);
    }

    [Fact]
    public async Task GetDashboardAsync_ProjectsApplicationCommandCenterSignals()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        var gmailInbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "alex.personal@example.com"
        };

        var reviewApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Unknown Company",
            Role = "Unknown Role",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 22, 9, 0, 0, TimeSpan.Zero)
        };
        var actionApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "OpenAI",
            Role = "Software Engineering Intern",
            Source = "Email",
            Status = ApplicationStatus.OaPending,
            Priority = 5,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 23, 9, 0, 0, TimeSpan.Zero)
        };
        var reviewEmail = new InboxEmail
        {
            InboxAccountId = gmailInbox.Id,
            InboxAccount = gmailInbox,
            JobApplicationId = reviewApplication.Id,
            JobApplication = reviewApplication,
            ExternalMessageId = "review-message-001",
            ThreadId = "review-thread-001",
            Sender = "recruiting@example.com",
            Subject = "Application update",
            RawBody = "The company and role could not be trusted.",
            ParsedBody = "The company and role could not be trusted.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 22, 8, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.GenericUpdate,
            ClassificationConfidence = 0.42m,
            RequiresManualReview = true,
            ReviewReason = "Company could not be extracted confidently.",
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 22, 8, 5, 0, TimeSpan.Zero)
        };
        var actionEmail = new InboxEmail
        {
            InboxAccountId = gmailInbox.Id,
            InboxAccount = gmailInbox,
            JobApplicationId = actionApplication.Id,
            JobApplication = actionApplication,
            ExternalMessageId = "openai-oa-001",
            ThreadId = "openai-thread-001",
            Sender = "assessments@openai.com",
            Subject = "Complete your assessment",
            RawBody = "Please complete the assessment before the deadline.",
            ParsedBody = "Please complete the assessment before the deadline.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 23, 8, 0, 0, TimeSpan.Zero),
            DueDateUtc = new DateTimeOffset(2026, 4, 24, 9, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.OaInvitation,
            ActionRequired = true,
            ClassificationConfidence = 0.94m,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 23, 8, 5, 0, TimeSpan.Zero)
        };
        var reminder = new Reminder
        {
            JobApplicationId = actionApplication.Id,
            JobApplication = actionApplication,
            Type = ReminderType.AssessmentDeadline,
            Status = ReminderStatus.Open,
            Title = "Complete assessment for OpenAI",
            DueAtUtc = new DateTimeOffset(2026, 4, 24, 9, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.AddRange(reviewApplication, actionApplication);
        dbContext.InboxEmails.AddRange(reviewEmail, actionEmail);
        dbContext.Reminders.Add(reminder);
        dbContext.JobApplicationEvents.AddRange(
            new JobApplicationEvent
            {
                JobApplicationId = reviewApplication.Id,
                JobApplication = reviewApplication,
                InboxEmailId = reviewEmail.Id,
                InboxEmail = reviewEmail,
                EventType = "generic_update",
                EventTimeUtc = new DateTimeOffset(2026, 4, 22, 9, 0, 0, TimeSpan.Zero)
            },
            new JobApplicationEvent
            {
                JobApplicationId = actionApplication.Id,
                JobApplication = actionApplication,
                InboxEmailId = actionEmail.Id,
                InboxEmail = actionEmail,
                EventType = "oa_invitation",
                EventTimeUtc = new DateTimeOffset(2026, 4, 23, 9, 0, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(1, response.Metrics.NeedsReview);
        Assert.Equal(1, response.Metrics.ReviewQueue);

        var reviewCard = Assert.Single(response.Applications, application => application.Id == reviewApplication.Id);
        Assert.True(reviewCard.RequiresReview);
        Assert.Equal(0.42m, reviewCard.ParserConfidence);
        Assert.Equal("Company could not be extracted confidently.", reviewCard.ReviewReason);
        Assert.Equal("Fix company / role in review", reviewCard.NextAction?.Title);
        Assert.Equal("review", reviewCard.NextAction?.Tone);
        Assert.Equal("High", reviewCard.PriorityLabel);
        Assert.Equal("Company could not be extracted confidently.", reviewCard.PriorityReason);
        Assert.Equal("Agent classified the latest evidence as a recruiting update.", reviewCard.LatestEvidence?.Summary);
        Assert.Null(reviewCard.LatestEvidence?.Subject);
        Assert.Null(reviewCard.LatestEvidence?.Sender);
        Assert.Null(reviewCard.LatestEvidence?.Snippet);

        var actionCard = Assert.Single(response.Applications, application => application.Id == actionApplication.Id);
        Assert.False(actionCard.RequiresReview);
        Assert.Equal(0.94m, actionCard.ParserConfidence);
        Assert.Equal("Complete assessment for OpenAI", actionCard.NextAction?.Title);
        Assert.Equal(reminder.DueAtUtc, actionCard.NextAction?.DueAtUtc);
        Assert.True(actionCard.NextAction?.HasExplicitDueDate);
        Assert.Equal(reminder.Id, actionCard.NextAction?.SourceReminderId);
        Assert.Equal("Urgent", actionCard.PriorityLabel);
        Assert.Equal("Open reminder is due within 2 days.", actionCard.PriorityReason);
        Assert.Equal(
            "Agent classified the latest evidence as an assessment or questionnaire request.",
            actionCard.LatestEvidence?.Summary);
        Assert.Null(actionCard.LatestEvidence?.Subject);

        var pendingAction = Assert.Single(response.PendingActions);
        Assert.Equal("assessment", pendingAction.TaskType);
        Assert.Equal("Complete your assessment", pendingAction.SourceSubject);
        Assert.Equal("assessments@openai.com", pendingAction.SourceSender);
        Assert.Equal(actionEmail.ReceivedAtUtc, pendingAction.ReceivedAtUtc);
        Assert.True(pendingAction.DeadlineFound);
        Assert.Null(pendingAction.DeadlineHint);
    }

    [Fact]
    public async Task GetDashboardAsync_ExcludesManualReviewBlockedReminders_FromPendingActions()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        var gmailInbox = new InboxAccount
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
            Company = "The AES Corporation",
            Role = "Summer Intern",
            Source = "Email",
            Status = ApplicationStatus.OaPending,
            Priority = 5,
            CreatedAtUtc = DashboardClock.UtcNow.AddDays(-1),
            LastActivityAtUtc = DashboardClock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(
            new InboxEmail
            {
                InboxAccountId = gmailInbox.Id,
                InboxAccount = gmailInbox,
                JobApplicationId = application.Id,
                JobApplication = application,
                ExternalMessageId = "gmail-review-action-1",
                ThreadId = "gmail-thread-review-action-1",
                Sender = "assessment@example.com",
                Subject = "Complete your assessment",
                RawBody = "Please complete the assessment.",
                ParsedBody = "Please complete the assessment.",
                ReceivedAtUtc = DashboardClock.UtcNow.AddHours(-2),
                Category = EmailCategory.OaInvitation,
                ActionRequired = true,
                RequiresManualReview = true,
                ProcessedAtUtc = DashboardClock.UtcNow.AddHours(-1)
            });
        dbContext.Reminders.Add(
            new Reminder
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                Type = ReminderType.AssessmentDeadline,
                Status = ReminderStatus.Open,
                Title = "Complete assessment for The AES Corporation",
                DueAtUtc = DashboardClock.UtcNow.AddDays(1)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(1, response.Metrics.ReviewQueue);
        Assert.Equal(0, response.Metrics.NeedsAction);
        Assert.Empty(response.PendingActions);
        Assert.False(response.Applications.Single().ActionRequired);
    }

    [Fact]
    public async Task GetDashboardAsync_ExcludesTerminalApplicationReminders_FromPendingActions()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var gmailInbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "alex.personal@example.com",
            LastSyncedAtUtc = new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero)
        };
        var rejectedApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Roblox",
            Role = "People Data Scientist Intern",
            Source = "Email",
            Status = ApplicationStatus.Rejected,
            Priority = 1,
            CreatedAtUtc = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 3, 27, 16, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.Add(rejectedApplication);
        dbContext.Reminders.Add(
            new Reminder
            {
                JobApplicationId = rejectedApplication.Id,
                JobApplication = rejectedApplication,
                Type = ReminderType.FollowUp,
                Status = ReminderStatus.Open,
                Title = "Review next step for Roblox",
                DueAtUtc = new DateTimeOffset(2026, 4, 16, 16, 11, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(0, response.Metrics.NeedsAction);
        Assert.Empty(response.PendingActions);
        Assert.Single(response.Applications);
        Assert.False(response.Applications[0].ActionRequired);
    }

    [Fact]
    public async Task GetDashboardAsync_ExcludesStaleOverdueReminders_FromPendingActions()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var staleApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "HDSI Lab",
            Role = "Fellow",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = DashboardClock.UtcNow.AddMonths(-8),
            LastActivityAtUtc = DashboardClock.UtcNow.AddMonths(-6)
        };
        var recentOverdueApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Carrier Global Corporation",
            Role = "Software Engineering Intern",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = DashboardClock.UtcNow.AddDays(-12),
            LastActivityAtUtc = DashboardClock.UtcNow.AddDays(-1)
        };
        var upcomingApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Roblox",
            Role = "Product Management Intern",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = DashboardClock.UtcNow.AddDays(-4),
            LastActivityAtUtc = DashboardClock.UtcNow.AddDays(-1)
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.AddRange(staleApplication, recentOverdueApplication, upcomingApplication);
        dbContext.Reminders.AddRange(
            new Reminder
            {
                JobApplicationId = staleApplication.Id,
                JobApplication = staleApplication,
                Type = ReminderType.FollowUp,
                Status = ReminderStatus.Open,
                Title = "Reply to recruiter at HDSI Lab 3.0",
                DueAtUtc = DashboardClock.UtcNow.AddMonths(-6)
            },
            new Reminder
            {
                JobApplicationId = recentOverdueApplication.Id,
                JobApplication = recentOverdueApplication,
                Type = ReminderType.FollowUp,
                Status = ReminderStatus.Open,
                Title = "Complete assessment for Carrier Global Corporation",
                DueAtUtc = DashboardClock.UtcNow.AddDays(-1)
            },
            new Reminder
            {
                JobApplicationId = upcomingApplication.Id,
                JobApplication = upcomingApplication,
                Type = ReminderType.AssessmentDeadline,
                Status = ReminderStatus.Open,
                Title = "Complete assessment for Roblox",
                DueAtUtc = DashboardClock.UtcNow.AddDays(1)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(2, response.Metrics.NeedsAction);
        Assert.DoesNotContain(response.PendingActions, action => action.Company == "HDSI Lab");
        Assert.Collection(
            response.PendingActions,
            action => Assert.Equal("Carrier Global Corporation", action.Company),
            action => Assert.Equal("Roblox", action.Company));
    }

    [Fact]
    public async Task GetDashboardAsync_ExcludesFarFutureReminders_FromPendingActions()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var nearApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Roblox",
            Role = "Product Management Intern",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = DashboardClock.UtcNow.AddDays(-4),
            LastActivityAtUtc = DashboardClock.UtcNow.AddDays(-1)
        };
        var futureApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "HDSI Lab 3.0",
            Role = "Fellow",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = DashboardClock.UtcNow.AddDays(-2),
            LastActivityAtUtc = DashboardClock.UtcNow.AddDays(-1)
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.AddRange(nearApplication, futureApplication);
        dbContext.Reminders.AddRange(
            new Reminder
            {
                JobApplicationId = nearApplication.Id,
                JobApplication = nearApplication,
                Type = ReminderType.AssessmentDeadline,
                Status = ReminderStatus.Open,
                Title = "Complete assessment for Roblox",
                DueAtUtc = DashboardClock.UtcNow.AddDays(1)
            },
            new Reminder
            {
                JobApplicationId = futureApplication.Id,
                JobApplication = futureApplication,
                Type = ReminderType.FollowUp,
                Status = ReminderStatus.Open,
                Title = "Reply to recruiter at HDSI Lab 3.0",
                DueAtUtc = DashboardClock.UtcNow.AddMonths(6)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        var pendingAction = Assert.Single(response.PendingActions);
        Assert.Equal("Roblox", pendingAction.Company);
        Assert.Equal(1, response.Metrics.NeedsAction);
    }

    [Fact]
    public async Task GetDashboardAsync_RearmsSnoozedReminders_WhenDue()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var dueApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Stripe",
            Role = "Backend Engineer",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = DashboardClock.UtcNow.AddDays(-4),
            LastActivityAtUtc = DashboardClock.UtcNow.AddDays(-1)
        };
        var futureApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Linear",
            Role = "Product Engineer",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = DashboardClock.UtcNow.AddDays(-4),
            LastActivityAtUtc = DashboardClock.UtcNow.AddDays(-1)
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.AddRange(dueApplication, futureApplication);
        dbContext.Reminders.AddRange(
            new Reminder
            {
                JobApplicationId = dueApplication.Id,
                JobApplication = dueApplication,
                Type = ReminderType.FollowUp,
                Status = ReminderStatus.Snoozed,
                Title = "Follow up with Stripe",
                DueAtUtc = DashboardClock.UtcNow.AddMinutes(-5)
            },
            new Reminder
            {
                JobApplicationId = futureApplication.Id,
                JobApplication = futureApplication,
                Type = ReminderType.FollowUp,
                Status = ReminderStatus.Snoozed,
                Title = "Follow up with Linear",
                DueAtUtc = DashboardClock.UtcNow.AddHours(2)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        var pendingAction = Assert.Single(response.PendingActions);
        Assert.Equal("Stripe", pendingAction.Company);
        Assert.Equal(1, response.Metrics.NeedsAction);
    }

    [Fact]
    public async Task GetDashboardAsync_ExcludesLowSignalFallbackReminders_FromPendingActions()
    {
        var databaseName = Guid.NewGuid().ToString("N");

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
            Company = "Legrand North America",
            Role = "Applied Data Analytics Intern",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 6, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        dbContext.Reminders.AddRange(
            new Reminder
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                Type = ReminderType.FollowUp,
                Status = ReminderStatus.Open,
                Title = "Review next step for Legrand North America",
                DueAtUtc = new DateTimeOffset(2026, 4, 16, 16, 11, 0, TimeSpan.Zero)
            },
            new Reminder
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                Type = ReminderType.FollowUp,
                Status = ReminderStatus.Open,
                Title = "Reply to recruiter at Legrand North America",
                DueAtUtc = new DateTimeOffset(2026, 4, 16, 17, 0, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(1, response.Metrics.NeedsAction);
        var pendingAction = Assert.Single(response.PendingActions);
        Assert.Equal("Reply to recruiter at Legrand North America", pendingAction.Title);
        Assert.True(response.Applications[0].ActionRequired);
    }

    [Fact]
    public async Task GetDashboardAsync_IncludesGmailUrl_ForPendingActions()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var gmailInbox = new InboxAccount
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
            Company = "Carrier Global Corporation",
            Role = "Software Engineering Intern",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = DashboardClock.UtcNow.AddDays(-3),
            LastActivityAtUtc = DashboardClock.UtcNow.AddHours(-2)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(
            new InboxEmail
            {
                InboxAccountId = gmailInbox.Id,
                InboxAccount = gmailInbox,
                JobApplicationId = application.Id,
                JobApplication = application,
                ExternalMessageId = "gmail-message-1",
                ThreadId = "gmail-thread-1",
                Sender = "recruiting@carrier.com",
                Subject = "Complete your assessment",
                RawBody = "Please complete the assessment by tomorrow.",
                ParsedBody = "Please complete the assessment by tomorrow.",
                ReceivedAtUtc = DashboardClock.UtcNow.AddHours(-3),
                DueDateUtc = DashboardClock.UtcNow.AddDays(1),
                Category = EmailCategory.OaInvitation,
                ActionRequired = true,
                ProcessedAtUtc = DashboardClock.UtcNow.AddHours(-2)
            });
        dbContext.Reminders.Add(
            new Reminder
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                Type = ReminderType.AssessmentDeadline,
                Status = ReminderStatus.Open,
                Title = "Complete assessment for Carrier Global Corporation",
                DueAtUtc = DashboardClock.UtcNow.AddDays(1)
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        var pendingAction = Assert.Single(response.PendingActions);
        Assert.Equal(
            "https://mail.google.com/mail/?authuser=alex.personal%40example.com#all/gmail-thread-1",
            pendingAction.GmailUrl);
        Assert.Equal("gmail-thread-1", pendingAction.SourceThreadId);
        Assert.Equal("gmail-message-1", pendingAction.SourceMessageId);
        Assert.True(pendingAction.HasExplicitDueDate);
    }

    [Fact]
    public async Task GetDashboardAsync_UsesEarliestVisibleEmailReceivedTime_ForApplicationCards()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var gmailInbox = new InboxAccount
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
            Company = "TikTok",
            Role = "Machine Learning Engineer Intern",
            Source = "Email",
            Status = ApplicationStatus.Interviewing,
            Priority = 5,
            CreatedAtUtc = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 17, 22, 52, 54, TimeSpan.Zero)
        };
        var firstReceivedAtUtc = new DateTimeOffset(2026, 3, 16, 7, 27, 11, TimeSpan.Zero);

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(
            new InboxEmail
            {
                InboxAccountId = gmailInbox.Id,
                InboxAccount = gmailInbox,
                JobApplicationId = application.Id,
                JobApplication = application,
                ExternalMessageId = "tiktok-interview-1",
                ThreadId = "thread-tiktok-interview",
                Sender = "recruiter@bytedance.com",
                Subject = "Schedule an interview",
                RawBody = "Please schedule your interview.",
                ParsedBody = "Please schedule your interview.",
                ReceivedAtUtc = firstReceivedAtUtc,
                Category = EmailCategory.InterviewInvitation,
                ProcessedAtUtc = firstReceivedAtUtc.AddMinutes(5)
            });
        for (var index = 0; index < 2001; index++)
        {
            var receivedAtUtc = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero).AddMinutes(index);
            dbContext.InboxEmails.Add(
                new InboxEmail
                {
                    InboxAccountId = gmailInbox.Id,
                    InboxAccount = gmailInbox,
                    JobApplicationId = application.Id,
                    JobApplication = application,
                    ExternalMessageId = $"tiktok-interview-newer-{index:D4}",
                    ThreadId = $"thread-tiktok-interview-newer-{index:D4}",
                    Sender = "recruiter@bytedance.com",
                    Subject = "Re: Schedule an interview",
                    RawBody = "Following up.",
                    ParsedBody = "Following up.",
                    ReceivedAtUtc = receivedAtUtc,
                    Category = EmailCategory.InterviewConfirmation,
                    ProcessedAtUtc = receivedAtUtc.AddMinutes(3)
                });
        }
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        var applicationCard = Assert.Single(response.Applications);
        Assert.Equal(firstReceivedAtUtc, applicationCard.ReceivedAtUtc);
        Assert.Equal(application.LastActivityAtUtc, applicationCard.LastActivityAtUtc);
        Assert.Equal(
            "https://mail.google.com/mail/?authuser=alex.personal%40example.com#all/thread-tiktok-interview-newer-2000",
            applicationCard.GmailUrl);
        Assert.False(applicationCard.HasOutboundReply);
        Assert.Null(applicationCard.LastOutboundReplyAtUtc);
    }

    [Fact]
    public async Task GetDashboardAsync_MarksApplicationCardsWithOutboundReplies_AndActivityCopy()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var gmailInbox = new InboxAccount
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
            Company = "Veeva Systems",
            Role = "Software Engineering Internship",
            Source = "Email",
            Status = ApplicationStatus.RecruiterContact,
            Priority = 4,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero)
        };
        var inboundEmail = new InboxEmail
        {
            InboxAccountId = gmailInbox.Id,
            InboxAccount = gmailInbox,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "veeva-inbound-001",
            ThreadId = "thread-veeva-001",
            Sender = "recruiting@veeva.com",
            Subject = "Please reply with your availability",
            RawBody = "Please reply with your availability.",
            ParsedBody = "Please reply with your availability.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 22, 18, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.RecruiterOutreach,
            ActionRequired = false,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 22, 18, 2, 0, TimeSpan.Zero)
        };
        var outboundEmail = new InboxEmail
        {
            InboxAccountId = gmailInbox.Id,
            InboxAccount = gmailInbox,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "veeva-outbound-001",
            ThreadId = "thread-veeva-001",
            Sender = "alex.personal@example.com",
            Subject = "Re: Please reply with your availability",
            RawBody = "Tuesday afternoon works well for me.",
            ParsedBody = "Tuesday afternoon works well for me.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 23, 17, 30, 0, TimeSpan.Zero),
            IsOutbound = true,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 23, 17, 31, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.AddRange(inboundEmail, outboundEmail);
        dbContext.JobApplicationEvents.Add(
            new JobApplicationEvent
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                InboxEmailId = outboundEmail.Id,
                InboxEmail = outboundEmail,
                EventType = "gmail_reply_detected",
                EventTimeUtc = outboundEmail.ReceivedAtUtc
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        var applicationCard = Assert.Single(response.Applications);
        Assert.Equal(0, response.Metrics.NeedsAction);
        Assert.Equal(1, response.Metrics.WaitingOnEmployer);
        Assert.True(applicationCard.HasOutboundReply);
        Assert.Equal(outboundEmail.ReceivedAtUtc, applicationCard.LastOutboundReplyAtUtc);
        Assert.Equal(
            "Sent Gmail reply detected for Veeva Systems",
            Assert.Single(response.RecentActivity).Description);
    }

    [Fact]
    public async Task GetDashboardAsync_RoutesPersonNameApplicationsToReview_NotWaiting()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var gmailInbox = new InboxAccount
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
            Company = "Okafor, Dana",
            Role = "you",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 21, 18, 0, 0, TimeSpan.Zero)
        };
        var outboundEmail = new InboxEmail
        {
            InboxAccountId = gmailInbox.Id,
            InboxAccount = gmailInbox,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "personal-name-outbound-001",
            ThreadId = "thread-personal-name-001",
            Sender = "alex.personal@example.com",
            Subject = "Re: availability",
            RawBody = "Thanks for the update.",
            ParsedBody = "Thanks for the update.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 21, 17, 30, 0, TimeSpan.Zero),
            IsOutbound = true,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 21, 17, 31, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(outboundEmail);
        dbContext.JobApplicationEvents.Add(
            new JobApplicationEvent
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                InboxEmailId = outboundEmail.Id,
                InboxEmail = outboundEmail,
                EventType = "gmail_reply_detected",
                EventTimeUtc = outboundEmail.ReceivedAtUtc
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);

        var waitingResponse = await service.GetDashboardAsync(
            ApplicationDefaults.DefaultUserEmail,
            new DashboardQueryOptions(View: "waiting"));
        var reviewResponse = await service.GetDashboardAsync(
            ApplicationDefaults.DefaultUserEmail,
            new DashboardQueryOptions(View: "needs-review"));

        Assert.Empty(waitingResponse.Applications);
        Assert.Equal(0, waitingResponse.Metrics.WaitingOnEmployer);
        Assert.Equal(1, reviewResponse.Metrics.NeedsReview);
        var reviewCard = Assert.Single(reviewResponse.Applications);
        Assert.True(reviewCard.RequiresReview);
        Assert.Equal("Company and role look like parser errors.", reviewCard.ReviewReason);
        Assert.Equal("Fix company / role in review", reviewCard.NextAction?.Title);
    }

    [Fact]
    public async Task GetDashboardAsync_QueryIgnoresHiddenSourceProvider()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var gmailInbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "alex.personal@example.com"
        };
        var sourceOnlyMatch = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Hntb",
            Role = "Analyst",
            Source = "Greenhouse",
            Status = ApplicationStatus.Rejected,
            Priority = 1,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 22, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 23, 9, 0, 0, TimeSpan.Zero)
        };
        var visibleMatch = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Palantir",
            Role = "Engineer",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.AddRange(sourceOnlyMatch, visibleMatch);
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);

        var response = await service.GetDashboardAsync(
            ApplicationDefaults.DefaultUserEmail,
            new DashboardQueryOptions(View: "all", Query: "en"));

        var application = Assert.Single(response.Applications);
        Assert.Equal("Palantir", application.Company);
        Assert.Equal("Engineer", application.Role);
    }

    [Fact]
    public async Task GetDashboardAsync_IncludesReceivedTime_ForRecentActivity()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var gmailInbox = new InboxAccount
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
            Company = "IBM",
            Role = "AI/ML Engineering Internship",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 19, 23, 44, 0, TimeSpan.Zero)
        };
        var receivedAtUtc = new DateTimeOffset(2026, 4, 19, 23, 40, 0, TimeSpan.Zero);
        var eventTimeUtc = new DateTimeOffset(2026, 4, 19, 23, 44, 0, TimeSpan.Zero);
        var inboxEmail = new InboxEmail
        {
            InboxAccountId = gmailInbox.Id,
            InboxAccount = gmailInbox,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "ibm-update-1",
            ThreadId = "thread-ibm-update",
            Sender = "recruiting@ibm.com",
            Subject = "IBM application update",
            RawBody = "Update",
            ParsedBody = "Update",
            ReceivedAtUtc = receivedAtUtc,
            Category = EmailCategory.GenericUpdate,
            ProcessedAtUtc = eventTimeUtc
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(inboxEmail);
        dbContext.JobApplicationEvents.Add(
            new JobApplicationEvent
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                InboxEmailId = inboxEmail.Id,
                InboxEmail = inboxEmail,
                EventType = "generic_update",
                EventTimeUtc = eventTimeUtc
            });
        await dbContext.SaveChangesAsync();

        var service = new DashboardQueryService(dbContext, DashboardClock);
        var response = await service.GetDashboardAsync(ApplicationDefaults.DefaultUserEmail);

        var activity = Assert.Single(response.RecentActivity);
        Assert.Equal(receivedAtUtc, activity.ReceivedAtUtc);
        Assert.Equal(eventTimeUtc, activity.OccurredAtUtc);
    }
}
