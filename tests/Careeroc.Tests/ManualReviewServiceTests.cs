using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Contracts.Applications;
using Careeroc.Contracts.Emails;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class ManualReviewServiceTests
{
    [Fact]
    public async Task ManualReview_AllowsClearingAssignment_AndUpdatingApplicationStatus()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 20, 0, 0, TimeSpan.Zero));

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
            Provider = "mock",
            EmailAddress = user.Email
        };

        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Figma",
            Role = "Product Engineer",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            LastActivityAtUtc = clock.UtcNow.AddDays(-2)
        };

        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "figma-001",
            ThreadId = "thread-figma-001",
            Sender = "coordinator@figma.com",
            Subject = "Interview invitation: Product Engineer at Figma",
            ParsedBody = "We would love to schedule your first interview.",
            RawBody = "We would love to schedule your first interview.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-5),
            Category = EmailCategory.ApplicationReceived,
            ActionRequired = false
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            clock);

        var emailUpdated = await service.CorrectEmailAsync(
            email.Id,
            new CorrectEmailRequest
            {
                Category = "interview_invitation",
                ClearApplicationAssignment = true,
                Company = "Figma",
                Role = "Product Engineer",
                ActionRequired = true,
                DueDateUtc = clock.UtcNow.AddDays(1),
                Notes = "Manual correction after review"
            },
            user.Email);

        var applicationUpdated = await service.UpdateApplicationStatusAsync(
            application.Id,
            new UpdateApplicationStatusRequest
            {
                Status = "Interviewing",
                Priority = 5
            },
            user.Email);

        Assert.True(emailUpdated);
        Assert.True(applicationUpdated);

        Assert.Null(email.JobApplicationId);
        Assert.Equal(EmailCategory.InterviewInvitation, email.Category);
        Assert.True(email.ActionRequired);
        Assert.Equal("Manual correction after review", email.ProcessingNotes);

        Assert.Equal(ApplicationStatus.Interviewing, application.Status);
        Assert.Equal(5, application.Priority);
        Assert.Single(dbContext.JobApplicationEvents);
        Assert.Equal("manual_stage_update", dbContext.JobApplicationEvents.Single().EventType);
    }

    [Fact]
    public async Task CorrectEmailAsync_KeepUnassignedDoesNotCreateAnApplication()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 20, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var user = new User { Email = "demo@example.com", Name = "Alex Rivera" };
        var inboxAccount = new InboxAccount { UserId = user.Id, User = user, Provider = "demo", EmailAddress = "alex@example.com" };
        var email = new InboxEmail
        {
            InboxAccount = inboxAccount,
            ExternalMessageId = "kestrel-001",
            Sender = "jobs@kestrel.example",
            Subject = "Thanks for applying to Kestrel AI",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1),
            Category = EmailCategory.ApplicationReceived
        };
        dbContext.AddRange(user, inboxAccount, email);
        await dbContext.SaveChangesAsync();
        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            clock);

        var updated = await service.CorrectEmailAsync(
            email.Id,
            new CorrectEmailRequest
            {
                Category = "application_received",
                ClearApplicationAssignment = true,
                Company = "Kestrel AI",
                Role = "Backend Engineer"
            },
            user.Email);

        Assert.True(updated);
        Assert.Null(email.JobApplicationId);
        Assert.Empty(dbContext.JobApplications);
    }

    [Fact]
    public async Task UpdateApplicationStatusAsync_DismissesOpenReminders_WhenStatusBecomesTerminal()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 20, 0, 0, TimeSpan.Zero));

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
            Company = "Roblox",
            Role = "People Data Scientist Intern",
            Status = ApplicationStatus.Interviewing,
            Priority = 5,
            Source = "Email",
            LastActivityAtUtc = clock.UtcNow.AddDays(-2)
        };
        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.FollowUp,
            Status = ReminderStatus.Open,
            Title = "Review next step for Roblox",
            DueAtUtc = clock.UtcNow.AddDays(1)
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            clock);

        var updated = await service.UpdateApplicationStatusAsync(
            application.Id,
            new UpdateApplicationStatusRequest
            {
                Status = "Rejected"
            },
            user.Email);

        Assert.True(updated);
        Assert.Equal(ApplicationStatus.Rejected, application.Status);
        Assert.Equal(ReminderStatus.Dismissed, reminder.Status);
        Assert.Contains(dbContext.JobApplicationEvents, @event => @event.EventType == "manual_stage_update");
        Assert.Contains(dbContext.JobApplicationEvents, @event => @event.EventType == "reminder_dismissed");
    }

    [Fact]
    public async Task CorrectApplicationAsync_MarksRecordAsNotApplicationAndRequeuesEmails()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 20, 0, 0, TimeSpan.Zero));

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
            Provider = "gmail",
            EmailAddress = user.Email
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Unknown",
            Role = "Unknown",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            LastActivityAtUtc = clock.UtcNow.AddDays(-2)
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "noise-001",
            ThreadId = "thread-noise-001",
            Sender = "newsletter@example.com",
            Subject = "Thanks for reading",
            ParsedBody = "This was incorrectly parsed as an application.",
            RawBody = "This was incorrectly parsed as an application.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-4),
            Category = EmailCategory.ApplicationReceived,
            MatchingConfidence = 0.44m,
            ActionRequired = false
        };
        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Title = "Follow up",
            DueAtUtc = clock.UtcNow.AddDays(1),
            Status = ReminderStatus.Open
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(email);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            clock);

        var updated = await service.CorrectApplicationAsync(
            application.Id,
            new CorrectApplicationRequest
            {
                Kind = "not_an_application",
                Notes = "Newsletter false positive."
            },
            user.Email);

        Assert.True(updated);
        Assert.Equal(ApplicationStatus.Closed, application.Status);
        Assert.Null(email.JobApplicationId);
        Assert.Null(email.MatchingConfidence);
        Assert.True(email.RequiresManualReview);
        Assert.Equal("Application record was marked as not an application.", email.ReviewReason);
        Assert.Contains("Newsletter false positive.", email.ProcessingNotes, StringComparison.Ordinal);
        Assert.Equal(ReminderStatus.Dismissed, reminder.Status);
        Assert.Contains(dbContext.JobApplicationEvents, @event => @event.EventType == "not_an_application");
        Assert.Contains(dbContext.JobApplicationEvents, @event => @event.EventType == "reminder_dismissed");
    }

    [Fact]
    public async Task ManualReview_CreatesApplicationAndWorkflowArtifacts_ForCorrectedAmbiguousEmail()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 21, 0, 0, TimeSpan.Zero));

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
            Provider = "mock",
            EmailAddress = user.Email
        };

        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "atlassian-generic-001",
            ThreadId = "thread-atlassian-001",
            Sender = "updates@atlassian.com",
            Subject = "Quick update on your candidacy",
            ParsedBody = "Hi Alex, thanks again for chatting with our team. We would love to schedule your first interview.",
            RawBody = "Hi Alex, thanks again for chatting with our team. We would love to schedule your first interview.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-3),
            Category = EmailCategory.GenericUpdate,
            ExtractedCompany = "Atlassian",
            ExtractedRole = "Unknown Role",
            ClassificationConfidence = 0.42m,
            MatchingConfidence = 0.35m,
            RequiresManualReview = true,
            ReviewReason = "Role could not be extracted confidently."
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            clock);

        var updated = await service.CorrectEmailAsync(
            email.Id,
            new CorrectEmailRequest
            {
                Category = "interview_invitation",
                Company = "Atlassian",
                Role = "Senior Software Engineer",
                ActionRequired = true,
                DueDateUtc = clock.UtcNow.AddDays(2),
                Notes = "Reviewed and confirmed by hand."
            },
            user.Email);

        Assert.True(updated);

        var application = Assert.Single(dbContext.JobApplications);
        Assert.Equal("Atlassian", application.Company);
        Assert.Equal("Senior Software Engineer", application.Role);
        Assert.Equal(ApplicationStatus.Interviewing, application.Status);
        Assert.Equal(5, application.Priority);
        Assert.Equal(email.ReceivedAtUtc, application.LastActivityAtUtc);

        Assert.Equal(application.Id, email.JobApplicationId);
        Assert.Equal(EmailCategory.InterviewInvitation, email.Category);
        Assert.Equal("Senior Software Engineer", email.ExtractedRole);
        Assert.True(email.ActionRequired);
        Assert.False(email.RequiresManualReview);
        Assert.Null(email.ReviewReason);
        Assert.NotNull(email.ReviewedAtUtc);
        Assert.NotNull(email.ProcessedAtUtc);
        Assert.Equal(0.82m, email.MatchingConfidence);

        var reminder = Assert.Single(dbContext.Reminders);
        Assert.Equal(application.Id, reminder.JobApplicationId);
        Assert.Equal(ReminderType.InterviewPreparation, reminder.Type);

        var applicationEvent = Assert.Single(dbContext.JobApplicationEvents);
        Assert.Equal(application.Id, applicationEvent.JobApplicationId);
        Assert.Equal(email.Id, applicationEvent.InboxEmailId);
        Assert.Equal("interview_invitation", applicationEvent.EventType);
        Assert.Contains("\"manuallyCorrected\":true", applicationEvent.ExtractedDataJson);
    }

    [Fact]
    public async Task ManualReview_NormalizesNonUtcDueDates_BeforePersisting()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 21, 0, 0, TimeSpan.Zero));

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
            Provider = "gmail",
            EmailAddress = user.Email
        };

        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "veeva-review-001",
            ThreadId = "thread-veeva-review-001",
            Sender = "recruiting@veeva.com",
            Subject = "Reply with availability",
            ParsedBody = "Please reply with your availability.",
            RawBody = "Please reply with your availability.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-2),
            Category = EmailCategory.GenericUpdate,
            ExtractedCompany = "Veeva Systems",
            ExtractedRole = "Unknown Role",
            RequiresManualReview = true,
            ReviewReason = "Role could not be extracted confidently."
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            clock);

        var localDueDate = new DateTimeOffset(2026, 4, 14, 17, 45, 0, TimeSpan.FromHours(-7));
        var updated = await service.CorrectEmailAsync(
            email.Id,
            new CorrectEmailRequest
            {
                Category = "recruiter_outreach",
                Company = "Veeva Systems",
                Role = "Software Engineering Internship",
                ActionRequired = true,
                DueDateUtc = localDueDate,
                Notes = "Reviewed by hand."
            },
            user.Email);

        Assert.True(updated);

        var expectedDueDateUtc = localDueDate.ToUniversalTime();
        var reminder = Assert.Single(dbContext.Reminders);
        Assert.Equal(expectedDueDateUtc, email.DueDateUtc);
        Assert.Equal(expectedDueDateUtc, reminder.DueAtUtc);
    }

    [Fact]
    public async Task ManualReview_ClearsDueDate_WhenCorrectionMarksEmailAsNoAction()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 21, 0, 0, TimeSpan.Zero));

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
            Provider = "gmail",
            EmailAddress = user.Email
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "CIBC",
            Role = "2026 Summer Intern - Software Engineer",
            Status = ApplicationStatus.OaPending,
            Priority = 5,
            Source = "Email",
            LastActivityAtUtc = clock.UtcNow.AddDays(-1)
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "cibc-digest-001",
            ThreadId = "thread-cibc-digest-001",
            Sender = "mailbox.cibc-workdayactionrequired@cibc.com",
            Subject = "Workday Inbox - Your Daily Digest",
            ParsedBody = "Daily digest",
            RawBody = "Daily digest",
            ReceivedAtUtc = clock.UtcNow.AddDays(-1),
            Category = EmailCategory.OaInvitation,
            ActionRequired = true,
            DueDateUtc = clock.UtcNow.AddDays(7),
            ExtractedCompany = "CIBC",
            ExtractedRole = application.Role
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            clock);

        var updated = await service.CorrectEmailAsync(
            email.Id,
            new CorrectEmailRequest
            {
                Category = "generic_update",
                ActionRequired = false,
                ClearDueDate = true,
                Notes = "User correction: this email is not an OA assignment."
            },
            user.Email);

        Assert.True(updated);
        Assert.Equal(EmailCategory.GenericUpdate, email.Category);
        Assert.False(email.ActionRequired);
        Assert.Null(email.DueDateUtc);
        Assert.Equal("User correction: this email is not an OA assignment.", email.ProcessingNotes);
    }

    [Fact]
    public async Task ManualReview_AppendsGuidanceFeedback_WhenUserCorrectsEmail()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 21, 0, 0, TimeSpan.Zero));
        var guidanceStore = new RecordingGuidanceStore();

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
            Provider = "gmail",
            EmailAddress = user.Email
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "cibc-digest-002",
            ThreadId = "thread-cibc-digest-002",
            Sender = "mailbox.cibc-workdayactionrequired@cibc.com",
            Subject = "Workday Inbox - Your Daily Digest",
            ParsedBody = "You have inbox items due soon.",
            RawBody = "You have inbox items due soon.",
            ReceivedAtUtc = clock.UtcNow.AddDays(-1),
            Category = EmailCategory.OaInvitation,
            ActionRequired = true,
            DueDateUtc = clock.UtcNow.AddDays(7),
            ExtractedCompany = "CIBC",
            ExtractedRole = "2026 Summer Intern - Software Engineer"
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            guidanceStore,
            clock);

        var updated = await service.CorrectEmailAsync(
            email.Id,
            new CorrectEmailRequest
            {
                Category = "generic_update",
                ActionRequired = false,
                ClearDueDate = true,
                Notes = "User correction: this email is not an OA assignment."
            },
            user.Email);

        Assert.True(updated);
        var feedback = Assert.Single(guidanceStore.Feedback);
        Assert.Equal(EmailCategory.OaInvitation, feedback.PreviousCategory);
        Assert.Equal(EmailCategory.GenericUpdate, feedback.CorrectedCategory);
        Assert.True(feedback.PreviousActionRequired);
        Assert.False(feedback.CorrectedActionRequired);
        Assert.NotNull(feedback.PreviousDueDateUtc);
        Assert.Null(feedback.CorrectedDueDateUtc);
        Assert.Contains("not an OA", feedback.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualReview_AppendsGuidanceFeedback_WhenUserCorrectsCompanyOnly()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 21, 0, 0, TimeSpan.Zero));
        var guidanceStore = new RecordingGuidanceStore();

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
            Provider = "gmail",
            EmailAddress = user.Email
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "url-company-001",
            ThreadId = "thread-url-company-001",
            Sender = "forms-receipts-noreply@google.com",
            Subject = "Candidate questionnaire",
            ParsedBody = "Please complete this questionnaire.",
            RawBody = "Please complete this questionnaire.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-2),
            Category = EmailCategory.OaInvitation,
            ActionRequired = true,
            ExtractedCompany = "Https",
            ExtractedRole = "Software Intern"
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            guidanceStore,
            clock);

        var updated = await service.CorrectEmailAsync(
            email.Id,
            new CorrectEmailRequest
            {
                Company = ApplicationDefaults.UnknownCompany
            },
            user.Email);

        Assert.True(updated);
        var feedback = Assert.Single(guidanceStore.Feedback);
        Assert.Equal("Https", feedback.PreviousCompany);
        Assert.Equal(ApplicationDefaults.UnknownCompany, feedback.CorrectedCompany);
        Assert.Equal("Software Intern", feedback.PreviousRole);
        Assert.Equal("Software Intern", feedback.CorrectedRole);
    }

    [Fact]
    public async Task ManualReview_RejectsCrossUserMutations()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 4, 1, 0, 0, TimeSpan.Zero));

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

        var inboxAccount = new InboxAccount
        {
            UserId = owner.Id,
            User = owner,
            Provider = "gmail",
            EmailAddress = owner.Email
        };

        var application = new JobApplication
        {
            UserId = owner.Id,
            User = owner,
            Company = "Stripe",
            Role = "Backend Engineer",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            LastActivityAtUtc = clock.UtcNow.AddDays(-1)
        };

        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "stripe-001",
            ThreadId = "thread-stripe-001",
            Sender = "recruiting@stripe.com",
            Subject = "Stripe next steps",
            ParsedBody = "Please schedule time with the team.",
            RawBody = "Please schedule time with the team.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-4),
            Category = EmailCategory.RecruiterOutreach,
            ActionRequired = true
        };

        dbContext.Users.AddRange(owner, intruder);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewService(
            dbContext,
            new ApplicationWorkflowService(dbContext, clock),
            new Careeroc.Application.Services.Agents.FollowUpTaskAgentService(clock),
            clock);

        var emailUpdated = await service.CorrectEmailAsync(
            email.Id,
            new CorrectEmailRequest
            {
                Category = "interview_invitation",
                Company = "Stripe",
                Role = "Backend Engineer",
                Notes = "Attempted by another user."
            },
            intruder.Email);

        var applicationUpdated = await service.UpdateApplicationStatusAsync(
            application.Id,
            new UpdateApplicationStatusRequest
            {
                Status = "Interviewing",
                Priority = 5
            },
            intruder.Email);

        Assert.False(emailUpdated);
        Assert.False(applicationUpdated);
        Assert.Equal(EmailCategory.RecruiterOutreach, email.Category);
        Assert.Equal(ApplicationStatus.Applied, application.Status);
        Assert.Empty(dbContext.JobApplicationEvents);
    }

    private sealed class RecordingGuidanceStore : IRecruitingEmailGuidanceStore
    {
        public List<RecruitingEmailGuidanceFeedback> Feedback { get; } = [];

        public Task<string> ReadGuidanceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Empty);
        }

        public Task AppendFeedbackAsync(
            RecruitingEmailGuidanceFeedback feedback,
            CancellationToken cancellationToken = default)
        {
            Feedback.Add(feedback);
            return Task.CompletedTask;
        }
    }
}
