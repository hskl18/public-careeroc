using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace Careeroc.Tests;

public sealed class EmailIntelligenceServiceTests
{
    [Fact]
    public async Task ProcessPendingAsync_CreatesApplicationAndReminder_ForAssessmentInvitation()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "ramp-oa-001",
            ThreadId = "thread-ramp-001",
            Sender = "recruiting@ramp.com",
            Subject = "Ramp Online Assessment for Software Engineer",
            ParsedBody = "Please complete the online assessment for the Software Engineer role by April 10, 2026.",
            RawBody = "Please complete the online assessment for the Software Engineer role by April 10, 2026.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-2)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(1, result.ApplicationsCreated);
        Assert.Equal(1, result.RemindersCreated);

        var application = Assert.Single(dbContext.JobApplications);
        Assert.Equal("Ramp", application.Company);
        Assert.Equal("Software Engineer", application.Role);
        Assert.Equal(ApplicationStatus.OaPending, application.Status);

        var reminder = Assert.Single(dbContext.Reminders);
        Assert.Equal(ReminderType.AssessmentDeadline, reminder.Type);
        Assert.Equal(application.Id, reminder.JobApplicationId);

        Assert.Equal(application.Id, email.JobApplicationId);
        Assert.Equal(EmailCategory.OaInvitation, email.Category);
        Assert.True(email.ActionRequired);
        Assert.Equal("recruiting@ramp.com", email.ContactEmail);
        var contact = Assert.Single(dbContext.RecruitingContacts);
        Assert.Equal("recruiting@ramp.com", contact.Email);
        Assert.Equal("Ramp", contact.CompanyHint);
        Assert.Equal(contact.Id, email.RecruitingContactId);
        Assert.Equal(1, contact.MessageCount);

        var trace = InboxEmailAgentTraceJson.Deserialize(email.ProcessingTraceJson);
        Assert.NotNull(trace);
        Assert.Collection(
            trace!.Steps,
            step => Assert.Equal("triage", step.Stage),
            step => Assert.Equal("workflow_extraction", step.Stage),
            step => Assert.Equal("recruiter_identity", step.Stage),
            step => Assert.Equal("scam_checker", step.Stage),
            step => Assert.Equal("application_match", step.Stage),
            step => Assert.Equal("review_evidence", step.Stage),
            step => Assert.Equal("follow_up_task", step.Stage));
        Assert.Equal(
            "Workflow Extraction Agent: Heuristic parser",
            InboxEmailAgentTraceJson.ExtractProcessingSource(trace));
    }

    [Fact]
    public async Task ProcessPendingAsync_DefersAJobWhileAnotherWorkerHoldsTheUsersLease()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var user = new User { Email = "demo@example.com", Name = "Alex Rivera" };
        var inboxAccount = new InboxAccount { UserId = user.Id, User = user, Provider = "mock", EmailAddress = user.Email };
        var email = new InboxEmail
        {
            InboxAccount = inboxAccount,
            ExternalMessageId = "ramp-oa-002",
            ThreadId = "thread-ramp-002",
            Sender = "recruiting@ramp.com",
            Subject = "Ramp Online Assessment for Software Engineer",
            ParsedBody = "Please complete the online assessment for the Software Engineer role.",
            RawBody = "Please complete the online assessment for the Software Engineer role.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-2)
        };
        dbContext.AddRange(user, inboxAccount, email);
        await dbContext.SaveChangesAsync();
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            new ApplicationWorkflowService(dbContext, clock),
            clock,
            NullLogger<EmailIntelligenceService>.Instance,
            new BusyLeaseManager());

        var result = await service.ProcessPendingAsync();

        var job = Assert.Single(dbContext.EmailProcessingJobs);
        Assert.Equal(0, result.JobsProcessed);
        Assert.Empty(dbContext.JobApplications);
        Assert.Equal(EmailProcessingStatus.Pending, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.True(job.AvailableAtUtc > clock.UtcNow);
    }

    [Fact]
    public async Task ProcessPendingAsync_AnOfferClosesInterviewPrepAndAsksForAnOfferReview()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var user = new User { Email = "demo@example.com", Name = "Alex Rivera" };
        var inboxAccount = new InboxAccount { UserId = user.Id, User = user, Provider = "mock", EmailAddress = user.Email };
        InboxEmail Email(string id, string subject, string body, int hoursAgo) => new()
        {
            InboxAccount = inboxAccount,
            ExternalMessageId = id,
            ThreadId = id,
            Sender = "recruiting@ramp.com",
            Subject = subject,
            ParsedBody = body,
            RawBody = body,
            ReceivedAtUtc = clock.UtcNow.AddHours(-hoursAgo)
        };
        var interview = Email(
            "ramp-interview",
            "Interview invitation: Software Engineer at Ramp",
            "We would like to schedule an interview for the Software Engineer role at Ramp. Please share your availability.",
            48);
        var offer = Email(
            "ramp-offer",
            "Offer: Software Engineer at Ramp",
            "We are pleased to offer you the Software Engineer role at Ramp. Your offer letter is attached.",
            2);
        dbContext.AddRange(user, inboxAccount, interview, offer);
        await dbContext.SaveChangesAsync();
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            new ApplicationWorkflowService(dbContext, clock),
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        await queue.EnqueueAsync(interview.Id);
        await service.ProcessPendingAsync();
        await queue.EnqueueAsync(offer.Id);
        await service.ProcessPendingAsync();

        var application = Assert.Single(dbContext.JobApplications);
        Assert.Equal(ApplicationStatus.Offer, application.Status);
        var reminders = dbContext.Reminders.OrderBy(reminder => reminder.CreatedAtUtc).ToList();
        Assert.Contains(reminders, reminder => reminder.Type == ReminderType.InterviewPreparation && reminder.Status == ReminderStatus.Dismissed);
        Assert.Contains(reminders, reminder => reminder.Title == "Review the offer from Ramp" && reminder.Status == ReminderStatus.Open);
    }

    private sealed class BusyLeaseManager : ILeaseManager
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_CreatesInterviewEvent_ForZoomAvailabilityRequest()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 27, 23, 10, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "jlee@example.edu",
            Name = "Jordan Lee"
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
            ExternalMessageId = "live-nation-interview-availability-001",
            ThreadId = "thread-live-nation-interview-availability-001",
            Sender = "Riley Park <RileyPark@paypal.com>",
            Subject = "PayPal Zoom Interview Availability Request",
            ParsedBody = """
                Hi Jordan,

                After sharing your resume with the team, they were impressed by your background and would like to invite you to interview via Zoom, to tell you a little more about the Data Operations Intern position and get to know you better.

                Please send over at least 3 dates with corresponding times and local time zone for interview this week/next week so that I can get you scheduled!
                """,
            RawBody = """
                Hi Jordan,

                After sharing your resume with the team, they were impressed by your background and would like to invite you to interview via Zoom, to tell you a little more about the Data Operations Intern position and get to know you better.

                Please send over at least 3 dates with corresponding times and local time zone for interview this week/next week so that I can get you scheduled!
                """,
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 27, 22, 57, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(1, result.ApplicationsCreated);
        Assert.Equal(1, result.RemindersCreated);

        var application = Assert.Single(dbContext.JobApplications);
        Assert.Equal("PayPal", application.Company);
        Assert.Equal("Data Operations Intern", application.Role);
        Assert.Equal(ApplicationStatus.Interviewing, application.Status);

        var applicationEvent = Assert.Single(dbContext.JobApplicationEvents);
        Assert.Equal("interview_invitation", applicationEvent.EventType);
        Assert.Equal(email.Id, applicationEvent.InboxEmailId);

        var reminder = Assert.Single(dbContext.Reminders);
        Assert.Equal(ReminderType.InterviewPreparation, reminder.Type);
        Assert.Equal(application.Id, reminder.JobApplicationId);

        Assert.Equal(application.Id, email.JobApplicationId);
        Assert.Equal(EmailCategory.InterviewInvitation, email.Category);
        Assert.True(email.ActionRequired);
        Assert.False(email.RequiresManualReview);
    }

    [Theory]
    [InlineData("Mara Okafor <mara.okafor@lumenrobotics.example>", "Interview confirmed", "Your interview is confirmed for Friday at 10:30 AM PT.", true)]
    [InlineData("Notion Careers <careers@notion.example>", "Interview confirmed: Notion", "Your interview with the Notion team is confirmed for Friday.", false)]
    public async Task ProcessPendingAsync_LinksThreadRepliesOnlyWhenTheEmployerMatches(
        string sender,
        string subject,
        string body,
        bool expectSameApplication)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var user = new User { Email = "jlee@example.edu", Name = "Jordan Lee" };
        var inboxAccount = new InboxAccount { UserId = user.Id, User = user, Provider = "gmail", EmailAddress = user.Email };
        var first = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "thread-continuity-001",
            ThreadId = "thread-continuity",
            Sender = "Lumen Robotics Careers <careers@lumenrobotics.example>",
            Subject = "Application received for Robotics Software Engineer at Lumen Robotics",
            ParsedBody = "Thank you for your application to Lumen Robotics.",
            ReceivedAtUtc = clock.UtcNow.AddDays(-5)
        };
        var reply = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "thread-continuity-002",
            ThreadId = "thread-continuity",
            Sender = sender,
            Subject = subject,
            ParsedBody = body,
            ReceivedAtUtc = clock.UtcNow.AddDays(-1)
        };
        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.AddRange(first, reply);
        await dbContext.SaveChangesAsync();
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(first.Id);
        await queue.EnqueueAsync(reply.Id);
        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            new ApplicationWorkflowService(dbContext, clock),
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        await service.ProcessPendingUntilIdleAsync();

        Assert.NotNull(first.JobApplicationId);
        Assert.Equal(expectSameApplication, reply.JobApplicationId == first.JobApplicationId);
    }

    [Fact]
    public async Task ProcessPendingAsync_BlocksWorkflowAutomation_WhenManualReviewIsRequired()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "aes-review-001",
            ThreadId = "thread-aes-review-001",
            Sender = "assessment@example.com",
            Subject = "Complete your assessment",
            ParsedBody = "Please complete the assessment for the internship.",
            RawBody = "Please complete the assessment for the internship.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new StubRecruitingEmailParser(
                new ParsedRecruitingEmail(
                    EmailCategory.OaInvitation,
                    ApplicationStatus.OaPending,
                    "The AES Corporation",
                    ApplicationDefaults.UnknownRole,
                    true,
                    null,
                    0.50m,
                    "Low-confidence assessment extraction.",
                    "Stub parser",
                    null)),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(0, result.ApplicationsCreated);
        Assert.Equal(0, result.ApplicationsUpdated);
        Assert.Equal(0, result.RemindersCreated);
        Assert.Empty(dbContext.JobApplications);
        Assert.Empty(dbContext.JobApplicationEvents);
        Assert.Empty(dbContext.Reminders);
        Assert.Null(email.JobApplicationId);
        Assert.True(email.RequiresManualReview);
        Assert.True(email.ActionRequired);
        Assert.Equal(EmailCategory.OaInvitation, email.Category);
    }

    [Fact]
    public async Task ProcessPendingAsync_BlocksWorkflowAutomation_WhenScamCheckerFindsHighRiskSignals()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 26, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "scam-offer-001",
            ThreadId = "thread-scam-offer-001",
            Sender = "recruiter.fake@gmail.com",
            Subject = "Remote software engineer offer",
            ParsedBody = "You are selected for a work from home software engineer role. We will mail a cashier's check so you can buy equipment from our vendor before onboarding.",
            RawBody = "You are selected for a work from home software engineer role. We will mail a cashier's check so you can buy equipment from our vendor before onboarding.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);
        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new StubRecruitingEmailParser(
                new ParsedRecruitingEmail(
                    EmailCategory.Offer,
                    ApplicationStatus.Offer,
                    "Fake Robotics",
                    "Software Engineer",
                    true,
                    null,
                    0.96m,
                    "High-confidence offer extraction.",
                    "Stub parser",
                    null)),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(0, result.ApplicationsCreated);
        Assert.Equal(0, result.ApplicationsUpdated);
        Assert.Equal(0, result.RemindersCreated);
        Assert.Empty(dbContext.JobApplications);
        Assert.Null(email.JobApplicationId);
        Assert.True(email.RequiresManualReview);
        Assert.Contains("Scam checker", email.ReviewReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("payment", email.ReviewReason, StringComparison.OrdinalIgnoreCase);

        var trace = InboxEmailAgentTraceJson.Deserialize(email.ProcessingTraceJson);
        Assert.NotNull(trace);
        var scamStep = Assert.Single(trace!.Steps, step => step.Stage == "scam_checker");
        Assert.Contains("blocked automation", scamStep.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(scamStep.Facts, fact => fact.Label == "Risk level" && fact.Value == "high");
    }

    [Fact]
    public async Task ProcessPendingAsync_DoesNotCreateApplication_FromTemporalCompanyPhrase()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "temporal-company-001",
            ThreadId = "thread-temporal-company-001",
            Sender = "no-reply@example.com",
            Subject = "Application update",
            ParsedBody = "We will not be moving forward with your application at this time.",
            RawBody = "We will not be moving forward with your application at this time.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new StubRecruitingEmailParser(
                new ParsedRecruitingEmail(
                    EmailCategory.Rejection,
                    ApplicationStatus.Rejected,
                    "This Time",
                    "AI/ML Engineer",
                    false,
                    null,
                    0.91m,
                    "Rejection detected from source email.",
                    "Stub parser",
                    null)),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(0, result.ApplicationsCreated);
        Assert.Equal(0, result.ApplicationsUpdated);
        Assert.Equal(0, result.RemindersCreated);
        Assert.Empty(dbContext.JobApplications);
        Assert.Empty(dbContext.JobApplicationEvents);
        Assert.Null(email.JobApplicationId);
        Assert.True(email.RequiresManualReview);
        Assert.Equal(ApplicationDefaults.UnknownCompany, email.ExtractedCompany);
    }

    [Fact]
    public async Task ProcessPendingAsync_PersistsExplicitRelativeDueDate_FromSourceEmail()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "veeva-relative-due-001",
            ThreadId = "thread-veeva-relative-due-001",
            Sender = "recruiting@veeva.com",
            Subject = "We thought you could be a strong fit for our internship",
            ParsedBody = "Hi Alex, we thought you could be a strong fit for the Software Engineering Internship at Veeva Systems. Please reply with your availability by tomorrow morning.",
            RawBody = "Hi Alex, we thought you could be a strong fit for the Software Engineering Internship at Veeva Systems. Please reply with your availability by tomorrow morning.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);

        var expectedDueDateUtc = new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero);
        var reminder = Assert.Single(dbContext.Reminders);
        Assert.Equal(expectedDueDateUtc, email.DueDateUtc);
        Assert.Equal(expectedDueDateUtc, reminder.DueAtUtc);
    }

    [Fact]
    public async Task ProcessPendingAsync_NormalizesNonUtcDueDates_BeforePersisting()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "veeva-follow-up-001",
            ThreadId = "thread-veeva-follow-up-001",
            Sender = "recruiting@veeva.com",
            Subject = "Please reply with your availability",
            ParsedBody = "Please reply with your availability by tomorrow morning.",
            RawBody = "Please reply with your availability by tomorrow morning.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);
        var localDueDate = new DateTimeOffset(2026, 4, 12, 9, 30, 0, TimeSpan.FromHours(-7));

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new StubRecruitingEmailParser(
                new ParsedRecruitingEmail(
                    EmailCategory.RecruiterOutreach,
                    ApplicationStatus.RecruiterContact,
                    "Veeva Systems",
                    "Software Engineering Internship",
                    true,
                    localDueDate,
                    0.94m,
                    "Detected recruiter outreach with a follow-up deadline.",
                    "Stub parser",
                    null)),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);

        var expectedDueDateUtc = localDueDate.ToUniversalTime();
        var reminder = Assert.Single(dbContext.Reminders);
        Assert.Equal(expectedDueDateUtc, email.DueDateUtc);
        Assert.Equal(expectedDueDateUtc, reminder.DueAtUtc);
        Assert.Equal("recruiting@veeva.com", email.ContactEmail);
        Assert.Equal(RecruitingContactType.Recruiter, email.ContactType);
        Assert.Equal("Recruiter", email.ContactRoleHint);
    }

    [Fact]
    public async Task ProcessPendingAsync_SuppressesReplyReminder_WhenSentReplyExistsInThread()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "jlee@example.edu",
            Name = "Jordan Lee"
        };

        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = "gmail",
            EmailAddress = user.Email
        };

        var inboundEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "veeva-reply-needed-001",
            ThreadId = "thread-veeva-reply-needed-001",
            Sender = "Recruiter <recruiting@veeva.com>",
            Subject = "Please reply with your availability",
            ParsedBody = "Please reply with your availability for the Software Engineering Internship by tomorrow.",
            RawBody = "Please reply with your availability for the Software Engineering Internship by tomorrow.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-2)
        };
        var sentReply = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "veeva-sent-reply-001",
            ThreadId = inboundEmail.ThreadId,
            Sender = "Jordan Lee <jlee@example.edu>",
            Subject = "Re: Please reply with your availability",
            ParsedBody = "Thanks, I am available Tuesday afternoon.",
            RawBody = "Thanks, I am available Tuesday afternoon.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1),
            IsOutbound = true,
            ActionRequired = false,
            ProcessedAtUtc = clock.UtcNow.AddMinutes(-30)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.AddRange(inboundEmail, sentReply);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(inboundEmail.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);
        var localDueDate = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.FromHours(-7));

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new StubRecruitingEmailParser(
                new ParsedRecruitingEmail(
                    EmailCategory.RecruiterOutreach,
                    ApplicationStatus.RecruiterContact,
                    "Veeva Systems",
                    "Software Engineering Internship",
                    true,
                    localDueDate,
                    0.96m,
                    "Detected recruiter outreach that asks for a reply.",
                    "Stub parser",
                    null)),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(1, result.ApplicationsCreated);
        Assert.Equal(0, result.RemindersCreated);
        Assert.Empty(dbContext.Reminders);

        var application = Assert.Single(dbContext.JobApplications);
        Assert.Equal(application.Id, inboundEmail.JobApplicationId);
        Assert.Equal(application.Id, sentReply.JobApplicationId);
        Assert.False(inboundEmail.ActionRequired);
        Assert.Null(inboundEmail.DueDateUtc);
        Assert.Contains("sent Gmail reply", inboundEmail.ProcessingNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessPendingAsync_ProcessesPassiveGenericRecruitingUpdate_WithoutManualReview()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ParsedBody = "Hi Alex, thanks again for chatting with our team. We will be in touch soon with next steps regarding your candidacy.",
            RawBody = "Hi Alex, thanks again for chatting with our team. We will be in touch soon with next steps regarding your candidacy.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(0, result.ApplicationsCreated);
        Assert.Empty(dbContext.JobApplications);
        Assert.Empty(dbContext.JobApplicationEvents);

        Assert.Null(email.JobApplicationId);
        Assert.Equal(EmailCategory.GenericUpdate, email.Category);
        Assert.Equal("Atlassian", email.ExtractedCompany);
        Assert.Equal("Unknown Role", email.ExtractedRole);
        Assert.Equal(0.42m, email.ClassificationConfidence);
        Assert.Equal(0.35m, email.MatchingConfidence);
        Assert.False(email.RequiresManualReview);
        Assert.Null(email.ReviewReason);
        Assert.Contains(ApplicationDefaults.UnknownRole, email.ProcessingNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessPendingAsync_DoesNotSurfacePassiveGenericUpdate_WhenExistingApplicationMatches()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            Company = "Trane Technologies",
            Role = "Developer Intern - 2601996",
            Source = "Email",
            Status = ApplicationStatus.Rejected,
            Priority = 1,
            LastActivityAtUtc = clock.UtcNow.AddDays(-1),
            CreatedAtUtc = clock.UtcNow.AddDays(-5)
        };

        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "trane-survey-001",
            ThreadId = "thread-trane-survey-001",
            Sender = "Recruiting@tranetechnologies.com",
            Subject = "Next steps regarding your candidacy",
            ParsedBody = "Thank you for your recent interest in joining Trane Technologies. As part of the next steps regarding your candidacy, please complete our recruitment survey.",
            RawBody = "Thank you for your recent interest in joining Trane Technologies. As part of the next steps regarding your candidacy, please complete our recruitment survey.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new StubRecruitingEmailParser(
                new ParsedRecruitingEmail(
                    EmailCategory.GenericUpdate,
                    ApplicationStatus.Applied,
                    "Trane Technologies",
                    ApplicationDefaults.UnknownRole,
                    false,
                    null,
                    0.95m,
                    "Parsed a recruiting survey update.",
                    "Stub parser",
                    null)),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(application.Id, email.JobApplicationId);
        Assert.False(email.RequiresManualReview);
        Assert.Null(email.ReviewReason);
    }

    [Fact]
    public async Task ProcessPendingAsync_CreatesPlaceholderApplication_ForApplicationReceiptWithoutReview()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "openai-application-unknown-role-001",
            ThreadId = "thread-openai-unknown-role-001",
            Sender = "careers@openai.com",
            Subject = "We received your application at OpenAI",
            ParsedBody = "Hi Alex, thank you for your interest in OpenAI. We received your application and will review it shortly.",
            RawBody = "Hi Alex, thank you for your interest in OpenAI. We received your application and will review it shortly.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-2)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(1, result.ApplicationsCreated);

        var application = Assert.Single(dbContext.JobApplications);
        Assert.Equal("OpenAI", application.Company);
        Assert.Equal(ApplicationDefaults.UnknownRole, application.Role);
        Assert.Equal(ApplicationStatus.Applied, application.Status);

        Assert.Equal(application.Id, email.JobApplicationId);
        Assert.Equal(EmailCategory.ApplicationReceived, email.Category);
        Assert.False(email.RequiresManualReview);
        Assert.Null(email.ReviewReason);
    }

    [Fact]
    public async Task ProcessPendingAsync_FiltersApplicationSecurityCode_BeforeParserRuns()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "greenhouse-security-code-001",
            ThreadId = "thread-greenhouse-security-code-001",
            Sender = "Greenhouse <no-reply@us.greenhouse-mail.io>",
            Subject = "Security code for your application to Crunchtime",
            ParsedBody = "Hi Jordan, Copy and paste this code into the security code field on your application: MWisHJAY. After you enter the code, resubmit your application.",
            RawBody = "Hi Jordan, Copy and paste this code into the security code field on your application: MWisHJAY. After you enter the code, resubmit your application.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new ThrowingRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Empty(dbContext.JobApplications);
        Assert.Null(email.Category);
        Assert.False(email.RequiresManualReview);
        Assert.False(email.ActionRequired);
        Assert.Null(email.JobApplicationId);
        Assert.Contains("Filtered inbox noise", email.ProcessingNotes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessPendingAsync_UsesSenderDisplayName_ForSmartRecruitersApplicationReceipt()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "nvidia-application-receipt-001",
            ThreadId = "thread-nvidia-application-receipt-001",
            Sender = "NVIDIA <notification@smartrecruiters.com>",
            Subject = "Thank you for applying to NVIDIA",
            ParsedBody = "Dear Jordan, Thank you for submitting your application for the position of Platform Engineering Intern - AI & Data Enablement - Hybrid. We will review all applications and contact suitable candidates.",
            RawBody = "Dear Jordan, Thank you for submitting your application for the position of Platform Engineering Intern - AI & Data Enablement - Hybrid. We will review all applications and contact suitable candidates.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(1, result.ApplicationsCreated);

        var application = Assert.Single(dbContext.JobApplications);
        Assert.Equal("NVIDIA", application.Company);
        Assert.Equal("Intern", application.Role);
        Assert.Equal(ApplicationStatus.Applied, application.Status);

        Assert.Equal(application.Id, email.JobApplicationId);
        Assert.Equal(EmailCategory.ApplicationReceived, email.Category);
        Assert.False(email.RequiresManualReview);
        Assert.Null(email.ReviewReason);
    }

    [Fact]
    public async Task ProcessPendingAsync_DoesNotCreateApplication_FromBareCampusDomainInference()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "jlee@example.edu",
            Name = "Jordan Lee"
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
            ExternalMessageId = "northgate-application-receipt-001",
            ThreadId = "thread-northgate-application-receipt-001",
            Sender = "\"Recruiting\" <recruiting@example.edu>",
            Subject = "Thank you for applying",
            ParsedBody = "Thank you for applying for the AI Intern role. We will review your materials soon.",
            RawBody = "Thank you for applying for the AI Intern role. We will review your materials soon.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(0, result.ApplicationsCreated);
        Assert.Empty(dbContext.JobApplications);
        Assert.Equal(ApplicationDefaults.UnknownCompany, email.ExtractedCompany);
        Assert.Equal(EmailCategory.ApplicationReceived, email.Category);
        Assert.Null(email.JobApplicationId);
    }

    [Fact]
    public async Task ProcessPendingAsync_BackfillsPlaceholderApplication_WhenLaterEmailProvidesRole()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            Company = "OpenAI",
            Role = ApplicationDefaults.UnknownRole,
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            LastActivityAtUtc = clock.UtcNow.AddDays(-1),
            CreatedAtUtc = clock.UtcNow.AddDays(-1)
        };

        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "openai-interview-role-known-001",
            ThreadId = "thread-openai-role-known-001",
            Sender = "recruiting@openai.com",
            Subject = "Schedule an interview for Backend Engineer at OpenAI",
            ParsedBody = "We'd love to schedule an interview for Backend Engineer at OpenAI next week. Please share your availability.",
            RawBody = "We'd love to schedule an interview for Backend Engineer at OpenAI next week. Please share your availability.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-3)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(0, result.ApplicationsCreated);
        Assert.Single(dbContext.JobApplications);

        Assert.Equal(application.Id, email.JobApplicationId);
        Assert.Equal("Backend Engineer", application.Role);
        Assert.Equal(ApplicationStatus.Interviewing, application.Status);
        Assert.False(email.RequiresManualReview);
    }

    [Fact]
    public async Task ProcessPendingAsync_CreatesSeparateApplication_WhenSameCompanyRoleDiffers()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "jlee@example.edu",
            Name = "Jordan Lee"
        };

        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = "gmail",
            EmailAddress = user.Email
        };

        var existingApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "TikTok",
            Role = "Machine Learning Engineer Intern - TikTok Search Team - 2026 Summer (BS/MS)",
            Source = "Email",
            Status = ApplicationStatus.Rejected,
            Priority = 1,
            LastActivityAtUtc = clock.UtcNow.AddDays(-2),
            CreatedAtUtc = clock.UtcNow.AddDays(-20)
        };

        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "tiktok-global-ecommerce-interview-001",
            ThreadId = "thread-tiktok-global-ecommerce-interview-001",
            Sender = "Recruiter <recruiter@bytedance.com>",
            Subject = "Reach Out: Opportunity with TikTok",
            ParsedBody = "We would like to schedule an interview for Machine Learning Engineer Intern - Global E-Commerce Content Recommendation - 2026 Summer (BS/MS) at TikTok.",
            RawBody = "We would like to schedule an interview for Machine Learning Engineer Intern - Global E-Commerce Content Recommendation - 2026 Summer (BS/MS) at TikTok.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(existingApplication);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new StubRecruitingEmailParser(
                new ParsedRecruitingEmail(
                    EmailCategory.InterviewInvitation,
                    ApplicationStatus.Interviewing,
                    "TikTok",
                    "Machine Learning Engineer Intern - Global E-Commerce Content Recommendation - 2026 Summer (BS/MS)",
                    true,
                    null,
                    0.96m,
                    "Detected interview scheduling for a distinct TikTok role.",
                    "Stub parser",
                    null)),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Equal(1, result.ApplicationsCreated);
        Assert.Equal(2, dbContext.JobApplications.Count());
        Assert.Equal(ApplicationStatus.Rejected, existingApplication.Status);

        var createdApplication = dbContext.JobApplications.Single(application => application.Id != existingApplication.Id);
        Assert.Equal("TikTok", createdApplication.Company);
        Assert.Equal(
            "Machine Learning Engineer Intern - Global E-Commerce Content Recommendation - 2026 Summer (BS/MS)",
            createdApplication.Role);
        Assert.Equal(ApplicationStatus.Interviewing, createdApplication.Status);
        Assert.Equal(createdApplication.Id, email.JobApplicationId);
        Assert.False(email.RequiresManualReview);
    }

    [Fact]
    public async Task ProcessPendingAsync_IgnoresGenericInboxNoise_InsteadOfSurfacingManualReview()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "robinhood-statement-001",
            ThreadId = "thread-robinhood-statement-001",
            Sender = "Robinhood <noreply@robinhood.com>",
            Subject = "Get an instant lift on retirement",
            ParsedBody = "View your account statement, manage preferences, and activate DashPass rewards now. This offer expires on April 30, 2026.",
            RawBody = "View your account statement, manage preferences, and activate DashPass rewards now. This offer expires on April 30, 2026.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Empty(dbContext.JobApplications);
        Assert.Null(email.Category);
        Assert.False(email.RequiresManualReview);
        Assert.False(email.ActionRequired);
        Assert.Null(email.JobApplicationId);
        Assert.Contains("Filtered inbox noise", email.ProcessingNotes, StringComparison.Ordinal);
        var trace = InboxEmailAgentTraceJson.Deserialize(email.ProcessingTraceJson);
        Assert.NotNull(trace);
        Assert.Single(trace!.Steps);
        Assert.Equal("triage", trace.Steps[0].Stage);
    }

    [Fact]
    public async Task ProcessPendingAsync_IgnoresCareerProgramEmail_BeforeParserRuns()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "projectbasta-program-001",
            ThreadId = "thread-projectbasta-program-001",
            Sender = "Code2Career <code2career@projectbasta.com>",
            Subject = "Final Extension Apply for 1on1 Mentorship with Google Engineer + Jobs Board Access!",
            ParsedBody = "We are opening a final application extension for the Spring cohort. This is a career readiness mentorship experience for first-generation computer science students with jobs board access.",
            RawBody = "We are opening a final application extension for the Spring cohort. This is a career readiness mentorship experience for first-generation computer science students with jobs board access.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new ThrowingRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Empty(dbContext.JobApplications);
        Assert.Null(email.Category);
        Assert.False(email.RequiresManualReview);
        Assert.False(email.ActionRequired);
        Assert.Null(email.JobApplicationId);
        Assert.Contains("Filtered inbox noise", email.ProcessingNotes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessPendingAsync_IgnoresHandshakeWebinarInvite_BeforeParserRuns()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 3, 18, 0, 0, TimeSpan.Zero));

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
            ExternalMessageId = "handshake-webinar-001",
            ThreadId = "thread-handshake-webinar-001",
            Sender = "Sara Felix via Handshake <handshake@notifications.joinhandshake.com>",
            Subject = "Don’t ghost this opportunity!",
            ParsedBody = "There is still time to register for tomorrow's BT Career Webinar. Join virtually from anywhere and register here.",
            RawBody = "There is still time to register for tomorrow's BT Career Webinar. Join virtually from anywhere and register here.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-1)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(email.Id);
        var workflowService = new ApplicationWorkflowService(dbContext, clock);

        var service = new EmailIntelligenceService(
            dbContext,
            queue,
            new ThrowingRecruitingEmailParser(),
            workflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);

        var result = await service.ProcessPendingAsync();

        Assert.Equal(1, result.JobsProcessed);
        Assert.Empty(dbContext.JobApplications);
        Assert.Null(email.Category);
        Assert.False(email.RequiresManualReview);
        Assert.False(email.ActionRequired);
        Assert.Null(email.JobApplicationId);
        Assert.Contains("Filtered inbox noise", email.ProcessingNotes, StringComparison.Ordinal);
    }

    private sealed class StubRecruitingEmailParser : IRecruitingEmailParser
    {
        private readonly ParsedRecruitingEmail _parsedEmail;

        public StubRecruitingEmailParser(ParsedRecruitingEmail parsedEmail)
        {
            _parsedEmail = parsedEmail;
        }

        public Task<ParsedRecruitingEmail> ParseAsync(
            InboxEmail inboxEmail,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_parsedEmail);
        }
    }

    private sealed class ThrowingRecruitingEmailParser : IRecruitingEmailParser
    {
        public Task<ParsedRecruitingEmail> ParseAsync(
            InboxEmail inboxEmail,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Parser should not run for filtered inbox noise.");
        }
    }
}
