using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class ApplicationDetailQueryServiceTests
{
    private static readonly TestClock Clock = new(new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetAsync_ReturnsAgentBriefThreadsAndFeedback_ForVisibleApplication()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };

        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };

        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "OpenAI",
            Role = "Research Engineer",
            Source = "Email",
            Status = ApplicationStatus.Interviewing,
            Priority = 5,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 10, 16, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 12, 18, 0, 0, TimeSpan.Zero)
        };

        var firstEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "openai-001",
            ThreadId = "thread-openai-001",
            Sender = "recruiting@openai.com",
            Subject = "Interview availability",
            ParsedBody = "Could you share your availability for a Research Engineer interview next week?",
            RawBody = "Could you share your availability for a Research Engineer interview next week?",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 12, 17, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.InterviewInvitation,
            ContactEmail = "recruiting@openai.com",
            ContactType = RecruitingContactType.Recruiter,
            ContactRoleHint = "Recruiter",
            ActionRequired = true,
            DueDateUtc = new DateTimeOffset(2026, 4, 14, 0, 0, 0, TimeSpan.Zero),
            ClassificationConfidence = 0.94m,
            MatchingConfidence = 0.96m,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 12, 17, 2, 0, TimeSpan.Zero)
        };

        var correctedEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "openai-002",
            ThreadId = "thread-openai-001",
            Sender = "coordinator@openai.com",
            Subject = "Interview confirmed",
            ParsedBody = "Thanks. Your interview is confirmed for Thursday.",
            RawBody = "Thanks. Your interview is confirmed for Thursday.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 12, 18, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.InterviewConfirmation,
            ContactEmail = "coordinator@openai.com",
            ContactType = RecruitingContactType.Coordinator,
            ContactRoleHint = "Recruiting Coordinator",
            ActionRequired = true,
            ClassificationConfidence = 0.88m,
            MatchingConfidence = 0.92m,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 12, 18, 2, 0, TimeSpan.Zero),
            ReviewedAtUtc = new DateTimeOffset(2026, 4, 12, 18, 5, 0, TimeSpan.Zero),
            ProcessingNotes = "Confirmed this is the same OpenAI interview loop and corrected the thread evidence."
        };

        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.InterviewPreparation,
            Status = ReminderStatus.Open,
            Title = "Prepare for interview with OpenAI",
            DueAtUtc = new DateTimeOffset(2026, 4, 13, 18, 0, 0, TimeSpan.Zero)
        };

        var applicationEvent = new JobApplicationEvent
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            InboxEmailId = correctedEmail.Id,
            InboxEmail = correctedEmail,
            EventType = "interview_confirmation",
            EventTimeUtc = new DateTimeOffset(2026, 4, 12, 18, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.AddRange(firstEmail, correctedEmail);
        dbContext.Reminders.Add(reminder);
        dbContext.JobApplicationEvents.Add(applicationEvent);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.GetAsync(application.Id, user.Email);

        Assert.NotNull(response);
        Assert.Equal("OpenAI", response!.Company);
        Assert.Equal("Research Engineer", response.Role);
        Assert.Equal(
            ["Applied", "Recruiter Contact", "Oa Pending", "Interviewing"],
            response.CompletedStages);
        Assert.Equal("A human follow-up is still open.", response.AgentBrief.Headline);
        Assert.Equal(2, response.Contacts.Count);
        Assert.Equal("Coordinator", response.Contacts[0].Type);
        Assert.Single(response.PendingActions);
        Assert.Equal(firstEmail.Id, response.PendingActions[0].SourceEmailId);
        Assert.Equal("thread-openai-001", response.PendingActions[0].SourceThreadId);
        Assert.Equal("openai-001", response.PendingActions[0].SourceMessageId);
        Assert.True(response.PendingActions[0].HasExplicitDueDate);
        Assert.Equal(
            "https://mail.google.com/mail/?authuser=demo%40example.com#all/thread-openai-001",
            response.PendingActions[0].GmailUrl);
        Assert.Single(response.Threads);
        Assert.Equal(
            "https://mail.google.com/mail/?authuser=demo%40example.com#all/thread-openai-001",
            response.Threads[0].GmailUrl);
        Assert.Equal(2, response.Threads[0].Messages.Count);
        Assert.Equal("Recruiter", response.Threads[0].Messages[0].ContactType);
        Assert.Equal(
            "https://mail.google.com/mail/?authuser=demo%40example.com#all/thread-openai-001",
            response.Threads[0].Messages[0].GmailUrl);
        Assert.Equal("Coordinator", response.Threads[0].Messages[1].ContactType);
        Assert.Single(response.FeedbackNotes);
        Assert.Contains("corrected the thread evidence", response.FeedbackNotes[0].Note, StringComparison.OrdinalIgnoreCase);
        Assert.Single(response.RecentActivity);
        Assert.Equal("interview_confirmation", response.RecentActivity[0].Category);
    }

    [Fact]
    public async Task GetAsync_SuppressesActions_ForTerminalApplication()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        var application = new JobApplication
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
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "roblox-001",
            ThreadId = "thread-roblox-001",
            Sender = "no-reply@roblox.com",
            Subject = "Thanks for applying to Roblox",
            ParsedBody = "We decided to move forward with other candidates.",
            RawBody = "We decided to move forward with other candidates.",
            ReceivedAtUtc = new DateTimeOffset(2026, 3, 27, 16, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.Rejection,
            ActionRequired = true,
            DueDateUtc = new DateTimeOffset(2026, 4, 16, 16, 11, 0, TimeSpan.Zero),
            RequiresManualReview = true,
            ClassificationConfidence = 0.95m,
            ProcessedAtUtc = new DateTimeOffset(2026, 3, 27, 16, 2, 0, TimeSpan.Zero)
        };
        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.FollowUp,
            Status = ReminderStatus.Open,
            Title = "Review next step for Roblox",
            DueAtUtc = new DateTimeOffset(2026, 4, 16, 16, 11, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(email);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.GetAsync(application.Id, user.Email);

        Assert.NotNull(response);
        Assert.False(response.ActionRequired);
        Assert.Empty(response.PendingActions);
        Assert.Equal("This application is rejected.", response.AgentBrief.Headline);
        Assert.Single(response.Threads);
        Assert.False(response.Threads[0].ActionRequired);
        Assert.False(response.Threads[0].Messages[0].ActionRequired);
        Assert.Null(response.Threads[0].Messages[0].DueDateUtc);
    }

    [Fact]
    public async Task GetAsync_PreservesInterviewStageHistory_ForRejectedApplications()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Enlyte",
            Role = "Software Development Intern",
            Source = "Email",
            Status = ApplicationStatus.Rejected,
            Priority = 1,
            CreatedAtUtc = new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 3, 26, 18, 0, 0, TimeSpan.Zero)
        };
        var interviewEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "enlyte-interview-001",
            ThreadId = "thread-enlyte-001",
            Sender = "recruiting@enlyte.com",
            Subject = "Interview availability",
            ParsedBody = "Please share your interview availability.",
            RawBody = "Please share your interview availability.",
            ReceivedAtUtc = new DateTimeOffset(2026, 3, 24, 18, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.InterviewInvitation,
            ProcessedAtUtc = new DateTimeOffset(2026, 3, 24, 18, 2, 0, TimeSpan.Zero)
        };
        var rejectionEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "enlyte-rejection-001",
            ThreadId = "thread-enlyte-001",
            Sender = "recruiting@enlyte.com",
            Subject = "Internship opportunity",
            ParsedBody = "We will not be moving forward.",
            RawBody = "We will not be moving forward.",
            ReceivedAtUtc = new DateTimeOffset(2026, 3, 26, 18, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.Rejection,
            ProcessedAtUtc = new DateTimeOffset(2026, 3, 26, 18, 2, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.AddRange(interviewEmail, rejectionEmail);
        dbContext.JobApplicationEvents.AddRange(
            new JobApplicationEvent
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                InboxEmailId = interviewEmail.Id,
                InboxEmail = interviewEmail,
                EventType = "interview_invitation",
                EventTimeUtc = interviewEmail.ReceivedAtUtc
            },
            new JobApplicationEvent
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                InboxEmailId = rejectionEmail.Id,
                InboxEmail = rejectionEmail,
                EventType = "rejection",
                EventTimeUtc = rejectionEmail.ReceivedAtUtc
            });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.GetAsync(application.Id, user.Email);

        Assert.NotNull(response);
        Assert.Equal("Rejected", response.Status);
        Assert.Equal(
            ["Applied", "Recruiter Contact", "Oa Pending", "Interviewing"],
            response.CompletedStages);
        Assert.DoesNotContain("Offer", response.CompletedStages);
    }

    [Fact]
    public async Task GetAsync_ReturnsReadableAgentSummaryAndCleanEmailExcerpt_ForThreadMessages()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "TikTok",
            Role = "Machine Learning Engineer Intern",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = new DateTimeOffset(2026, 3, 14, 8, 13, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 3, 14, 8, 13, 0, TimeSpan.Zero)
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "tiktok-application-001",
            ThreadId = "thread-tiktok-application-001",
            Sender = "noreply@careers.tiktok.com",
            Subject = "Thank you for applying to TikTok!",
            ParsedBody = "Hi Jordan Lee,<br/><br/>You did it! Great job!<br/><br/>Follow <a href=\"https://www.tiktok.com/@lifeattiktok?tracking=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789\">@lifeattiktok</a> for updates.",
            RawBody = "Hi Jordan Lee,<br/><br/>You did it! Great job!",
            ReceivedAtUtc = new DateTimeOffset(2026, 3, 14, 8, 13, 0, TimeSpan.Zero),
            Category = EmailCategory.ApplicationReceived,
            ActionRequired = false,
            ProcessingTraceJson = InboxEmailAgentTraceJson.Serialize(
                new InboxEmailAgentTrace(
                    new DateTimeOffset(2026, 3, 14, 8, 15, 0, TimeSpan.Zero),
                    [
                        new AgentTraceStep(
                            "workflow_extraction",
                            "Workflow Extraction Agent",
                            "Application receipt logged for TikTok.",
                            null,
                            [
                                new AgentTraceFact("Parsing source", "Gemma"),
                                new AgentTraceFact("Parsing model", "gemma4:31b")
                            ])
                    ])),
            ProcessedAtUtc = new DateTimeOffset(2026, 3, 14, 8, 15, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.GetAsync(application.Id, user.Email);

        Assert.NotNull(response);
        var message = Assert.Single(Assert.Single(response.Threads).Messages);
        Assert.Equal("Application receipt received for TikTok.", message.Summary);
        Assert.DoesNotContain("<br", message.BodyPreview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=", message.BodyPreview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", message.BodyPreview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You did it! Great job!", message.BodyPreview);
    }

    [Fact]
    public async Task GetAsync_SurfacesNewerActiveThread_WhenApplicationWasPreviouslyRejected()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "TikTok",
            Role = "Machine Learning Engineer Intern",
            Source = "Email",
            Status = ApplicationStatus.Rejected,
            Priority = 1,
            CreatedAtUtc = new DateTimeOffset(2026, 3, 14, 8, 13, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 20, 17, 47, 0, TimeSpan.Zero)
        };
        var rejectionEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "tiktok-rejection-001",
            ThreadId = "thread-tiktok-rejection-001",
            Sender = "no-reply@careers.tiktok.com",
            Subject = "Application update",
            ParsedBody = "We decided to move forward with other candidates.",
            RawBody = "We decided to move forward with other candidates.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.Rejection,
            ActionRequired = false,
            ClassificationConfidence = 0.93m,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 1, 12, 2, 0, TimeSpan.Zero)
        };
        var interviewEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "tiktok-interview-001",
            ThreadId = "thread-tiktok-interview-001",
            Sender = "noreply@careers.tiktok.com",
            Subject = "Confirmation of your TikTok technical interview",
            ParsedBody = "We are happy to confirm your technical interview for a general hire Machine Learning Engineer Intern role.",
            RawBody = "We are happy to confirm your technical interview for a general hire Machine Learning Engineer Intern role.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 20, 17, 47, 0, TimeSpan.Zero),
            Category = EmailCategory.InterviewConfirmation,
            ContactEmail = "noreply@careers.tiktok.com",
            ContactType = RecruitingContactType.Coordinator,
            ContactRoleHint = "Coordinator",
            ActionRequired = true,
            DueDateUtc = new DateTimeOffset(2026, 4, 22, 17, 0, 0, TimeSpan.Zero),
            ClassificationConfidence = 0.90m,
            MatchingConfidence = 0.72m,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 20, 17, 49, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.AddRange(rejectionEmail, interviewEmail);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.GetAsync(application.Id, user.Email);

        Assert.NotNull(response);
        Assert.Equal("Interviewing", response.Status);
        Assert.True(response.ActionRequired);
        Assert.Equal("The interview loop is active.", response.AgentBrief.Headline);
        Assert.Equal(2, response.Threads.Count);
        Assert.True(response.Threads[0].ActionRequired);
        Assert.True(response.Threads[0].Messages[0].ActionRequired);
        Assert.Equal(new DateTimeOffset(2026, 4, 22, 17, 0, 0, TimeSpan.Zero), response.Threads[0].Messages[0].DueDateUtc);
    }

    [Fact]
    public async Task GetAsync_ExcludesLowSignalFallbackReminders_FromPendingActions()
    {
        var databaseName = Guid.NewGuid().ToString("N");

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

        var service = CreateService(dbContext);
        var response = await service.GetAsync(application.Id, user.Email);

        Assert.NotNull(response);
        Assert.True(response.ActionRequired);
        var pendingAction = Assert.Single(response.PendingActions);
        Assert.Equal("Reply to recruiter at Legrand North America", pendingAction.Title);
        Assert.Null(pendingAction.SourceEmailId);
        Assert.False(pendingAction.HasExplicitDueDate);
        Assert.Null(pendingAction.GmailUrl);
    }

    [Fact]
    public async Task GetAsync_UsesSentReplyActivityCopy_WhenReplyEventExists()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
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
        var outboundEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "veeva-outbound-001",
            ThreadId = "thread-veeva-001",
            Sender = "demo@example.com",
            Subject = "Re: Please reply with your availability",
            ParsedBody = "Tuesday afternoon works for me.",
            RawBody = "Tuesday afternoon works for me.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 23, 17, 30, 0, TimeSpan.Zero),
            IsOutbound = true,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 23, 17, 31, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
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

        var service = CreateService(dbContext);
        var response = await service.GetAsync(application.Id, user.Email);

        Assert.NotNull(response);
        Assert.Single(response.RecentActivity);
        Assert.Equal(
            "Sent Gmail reply found for Veeva Systems",
            response.RecentActivity[0].Description);
    }

    [Fact]
    public async Task GetAsync_LimitsLinkedMessagesAndArtifactEvidence_ForLargeApplicationHistory()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Anthropic",
            Role = "Software Engineer",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);

        for (var index = 0; index < 275; index++)
        {
            dbContext.InboxEmails.Add(
                new InboxEmail
                {
                    InboxAccountId = inboxAccount.Id,
                    InboxAccount = inboxAccount,
                    JobApplicationId = application.Id,
                    JobApplication = application,
                    ExternalMessageId = $"anthropic-{index:D3}",
                    ThreadId = $"thread-anthropic-{index:D3}",
                    Sender = "recruiting@anthropic.com",
                    Subject = $"Anthropic update {index:D3}",
                    ParsedBody = $"Application update {index:D3}",
                    RawBody = $"Application update {index:D3}",
                    ReceivedAtUtc = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero).AddMinutes(index),
                    Category = EmailCategory.GenericUpdate,
                    ProcessedAtUtc = new DateTimeOffset(2026, 4, 1, 9, 1, 0, TimeSpan.Zero).AddMinutes(index)
                });
        }

        for (var index = 0; index < 60; index++)
        {
            dbContext.ApplicationArtifactEvidence.Add(
                new ApplicationArtifactEvidence
                {
                    UserId = user.Id,
                    JobApplicationId = application.Id,
                    ArtifactKind = "screenshot",
                    FileName = $"artifact-{index:D2}.png",
                    ContentType = "image/png",
                    SafeTitle = $"Artifact {index:D2}",
                    ExtractedFactType = "schedule",
                    ExtractedFactText = $"Interview artifact {index:D2}",
                    Confidence = 0.85m,
                    SourceSnippet = "Sanitized snippet.",
                    ContentHash = $"hash-{index:D2}",
                    EvidenceSource = "artifact_upload",
                    ModelPath = "model-router-fallback",
                    Purpose = "artifact_evidence_extraction",
                    ReviewGateResult = "evidence_recorded_no_mutation",
                    CreatedAtUtc = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero).AddMinutes(index)
                });
        }

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var response = await service.GetAsync(application.Id, user.Email);

        Assert.NotNull(response);
        var detail = response!;
        Assert.Equal(250, detail.Threads.Count);
        Assert.DoesNotContain(detail.Threads, thread => thread.ThreadId == "thread-anthropic-000");
        Assert.Contains(detail.Threads, thread => thread.ThreadId == "thread-anthropic-274");
        Assert.Equal(60, detail.ArtifactEvidenceTotalCount);
        Assert.Equal(0, detail.RecentActivityTotalCount);
        var artifactEvidence = detail.ArtifactEvidence ?? throw new InvalidOperationException("Artifact evidence should be present.");
        Assert.Equal(50, artifactEvidence.Count);
        Assert.DoesNotContain(artifactEvidence, evidence => evidence.FileName == "artifact-00.png");
        Assert.Contains(artifactEvidence, evidence => evidence.FileName == "artifact-59.png");
    }

    private static ApplicationDetailQueryService CreateService(Careeroc.Infrastructure.Persistence.CareerocDbContext dbContext)
    {
        return new ApplicationDetailQueryService(
            dbContext,
            new UserMemoryProposalService(dbContext, Clock));
    }
}
