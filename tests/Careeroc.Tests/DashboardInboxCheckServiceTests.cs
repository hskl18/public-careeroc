using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace Careeroc.Tests;

public sealed class DashboardInboxCheckServiceTests
{
    [Fact]
    public async Task CheckAsync_CacheHit_SkipsGmailFetch()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = AddConnectedGmailUser(dbContext, clock.UtcNow.AddSeconds(-30));
        await dbContext.SaveChangesAsync();

        var source = new CountingInboxEmailSource(
            [
                new ExternalInboxEmail(
                    "new-message",
                    "thread-new-message",
                    "recruiting@example.com",
                    "Application update",
                    "Thanks for applying.",
                    clock.UtcNow.AddMinutes(-5),
                    IsOutbound: false)
            ]);
        var service = CreateService(dbContext, clock, source);

        var result = await service.CheckAsync(user.Email, TimeSpan.FromMinutes(2));

        Assert.True(result.CacheHit);
        Assert.Equal(0, source.Calls);
        Assert.Equal(0, result.Sync.EmailsCreated);
        Assert.Empty(dbContext.InboxEmails);
    }

    [Fact]
    public async Task CheckAsync_CacheMiss_FetchesAndProcessesNewMail()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = AddConnectedGmailUser(dbContext, clock.UtcNow.AddMinutes(-10));
        await dbContext.SaveChangesAsync();

        var source = new CountingInboxEmailSource(
            [
                new ExternalInboxEmail(
                    "openai-application-received",
                    "thread-openai-application",
                    "recruiting@openai.com",
                    "OpenAI application received",
                    "Thank you for applying to the Software Engineering Internship at OpenAI.",
                    clock.UtcNow.AddMinutes(-4),
                    IsOutbound: false)
            ]);
        var service = CreateService(dbContext, clock, source);

        var result = await service.CheckAsync(user.Email, TimeSpan.FromMinutes(2));

        Assert.False(result.CacheHit);
        Assert.Equal(1, source.Calls);
        Assert.Equal(1, result.Sync.EmailsCreated);
        Assert.Equal(1, result.Sync.JobsEnqueued);
        Assert.Equal(1, result.Processing.JobsProcessed);
        Assert.Equal(1, result.Dashboard.Inbox.SyncedEmails);
        Assert.Equal(1, result.Dashboard.Inbox.ProcessedEmails);
    }

    [Fact]
    public async Task CheckAsync_CacheHit_StillProcessesAlreadyStoredUnprocessedMail()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = AddConnectedGmailUser(dbContext, clock.UtcNow.AddSeconds(-30));
        await dbContext.SaveChangesAsync();
        var inboxAccount = dbContext.InboxAccounts.Single();
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "stored-unprocessed",
            ThreadId = "thread-stored-unprocessed",
            Sender = "recruiting@example.com",
            Subject = "Application update",
            RawBody = "Unfortunately, we are not moving forward.",
            ParsedBody = "Unfortunately, we are not moving forward.",
            ReceivedAtUtc = clock.UtcNow.AddMinutes(-5)
        };
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var source = new CountingInboxEmailSource([]);
        var service = CreateService(dbContext, clock, source);

        var result = await service.CheckAsync(user.Email, TimeSpan.FromMinutes(2));

        Assert.True(result.CacheHit);
        Assert.Equal(0, source.Calls);
        Assert.Equal(1, result.StaleEmailsQueued);
        Assert.Equal(1, result.Processing.JobsProcessed);
        Assert.NotNull(email.ProcessedAtUtc);
    }

    [Fact]
    public async Task CheckAsync_CacheHit_RefreshesReminderEvidenceBeforeDashboard()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 24, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = AddConnectedGmailUser(dbContext, clock.UtcNow.AddSeconds(-30));
        var inboxAccount = user.InboxAccounts.Single();
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Carrier Global Corporation",
            Role = "Software Engineering Intern",
            Source = "Email",
            Status = ApplicationStatus.OaPending,
            Priority = 4,
            CreatedAtUtc = clock.UtcNow.AddDays(-1),
            LastActivityAtUtc = clock.UtcNow.AddHours(-4)
        };
        var reminder = new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = ReminderType.AssessmentDeadline,
            Title = "Complete assessment for Carrier Global Corporation",
            DueAtUtc = clock.UtcNow.AddDays(2),
            Status = ReminderStatus.Open,
            CreatedAtUtc = clock.UtcNow.AddHours(-4)
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "carrier-assessment",
            ThreadId = "thread-carrier-assessment",
            Sender = "assessment@carrier.com",
            Subject = "Complete your assessment",
            RawBody = "Please complete your assessment within 48 hours of receiving this invitation.",
            ParsedBody = "Please complete your assessment within 48 hours of receiving this invitation.",
            ReceivedAtUtc = clock.UtcNow.AddHours(-4),
            Category = EmailCategory.OaInvitation,
            ActionRequired = true,
            ProcessingNotes = $"{EmailAnalysisVersion.Marker} Seeded parser output.",
            ProcessedAtUtc = clock.UtcNow.AddHours(-3)
        };
        dbContext.JobApplications.Add(application);
        dbContext.Reminders.Add(reminder);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock, new CountingInboxEmailSource([]));

        var result = await service.CheckAsync(user.Email, TimeSpan.FromMinutes(2));

        var expectedDueDateUtc = clock.UtcNow.AddHours(-4).AddHours(48);
        var action = Assert.Single(result.Dashboard.PendingActions);
        Assert.True(result.CacheHit);
        Assert.Equal(expectedDueDateUtc, email.DueDateUtc);
        Assert.Equal(expectedDueDateUtc, reminder.DueAtUtc);
        Assert.True(action.HasExplicitDueDate);
        Assert.Equal(expectedDueDateUtc, action.DueAtUtc);
    }

    [Fact]
    public async Task CheckAsync_ProcessesOnlyCurrentUsersPendingJobs()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 18, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var currentUser = AddConnectedGmailUser(
            dbContext,
            clock.UtcNow.AddSeconds(-30),
            "current@example.edu");
        var otherUser = AddConnectedGmailUser(
            dbContext,
            clock.UtcNow.AddSeconds(-30),
            "other@example.edu");
        await dbContext.SaveChangesAsync();

        var currentInbox = currentUser.InboxAccounts.Single();
        var otherInbox = otherUser.InboxAccounts.Single();
        var currentEmail = new InboxEmail
        {
            InboxAccountId = currentInbox.Id,
            InboxAccount = currentInbox,
            ExternalMessageId = "current-user-message",
            ThreadId = "thread-current-user-message",
            Sender = "recruiting@example.com",
            Subject = "Application update",
            RawBody = "Thank you for applying to the Software Engineering Internship at OpenAI.",
            ParsedBody = "Thank you for applying to the Software Engineering Internship at OpenAI.",
            ReceivedAtUtc = clock.UtcNow.AddMinutes(-5)
        };
        var otherEmail = new InboxEmail
        {
            InboxAccountId = otherInbox.Id,
            InboxAccount = otherInbox,
            ExternalMessageId = "other-user-message",
            ThreadId = "thread-other-user-message",
            Sender = "recruiting@example.com",
            Subject = "Application update",
            RawBody = "Thank you for applying to the Data Internship at Stripe.",
            ParsedBody = "Thank you for applying to the Data Internship at Stripe.",
            ReceivedAtUtc = clock.UtcNow.AddMinutes(-5)
        };

        dbContext.InboxEmails.AddRange(currentEmail, otherEmail);
        await dbContext.SaveChangesAsync();

        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        await queue.EnqueueAsync(currentEmail.Id);
        await queue.EnqueueAsync(otherEmail.Id);

        var service = CreateService(dbContext, clock, new CountingInboxEmailSource([]));

        var result = await service.CheckAsync(currentUser.Email, TimeSpan.FromMinutes(2));

        Assert.True(result.CacheHit);
        Assert.Equal(1, result.Processing.JobsProcessed);
        await dbContext.Entry(currentEmail).ReloadAsync();
        await dbContext.Entry(otherEmail).ReloadAsync();
        Assert.NotNull(currentEmail.ProcessedAtUtc);
        Assert.Null(otherEmail.ProcessedAtUtc);

        var otherJob = dbContext.EmailProcessingJobs.Single(job => job.InboxEmailId == otherEmail.Id);
        Assert.Equal(EmailProcessingStatus.Pending, otherJob.Status);
    }

    private static User AddConnectedGmailUser(
        Careeroc.Infrastructure.Persistence.CareerocDbContext dbContext,
        DateTimeOffset? lastSyncedAtUtc,
        string? email = null)
    {
        var user = new User
        {
            Email = email ?? ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email,
            LastSyncedAtUtc = lastSyncedAtUtc
        };
        InboxAccountMetadataJson.Write(
            inboxAccount,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                AccessToken = "access-token",
                RefreshToken = "refresh-token"
            });

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);

        return user;
    }

    private static DashboardInboxCheckService CreateService(
        Careeroc.Infrastructure.Persistence.CareerocDbContext dbContext,
        TestClock clock,
        IInboxEmailSource source)
    {
        var queue = new DatabaseEmailProcessingQueue(dbContext, clock);
        var applicationWorkflowService = new ApplicationWorkflowService(dbContext, clock);
        var emailSyncService = new EmailSyncService(
            dbContext,
            source,
            queue,
            clock,
            NullLogger<EmailSyncService>.Instance);
        var emailAnalysisMaintenanceService = new EmailAnalysisMaintenanceService(dbContext, queue);
        var emailIntelligenceService = new EmailIntelligenceService(
            dbContext,
            queue,
            new HeuristicRecruitingEmailParser(),
            applicationWorkflowService,
            clock,
            NullLogger<EmailIntelligenceService>.Instance);
        var reminderEvidenceRefreshService = new ReminderEvidenceRefreshService(
            dbContext,
            new HeuristicDueDateExtractor(new HeuristicRecruitingEmailParser()),
            applicationWorkflowService,
            clock);

        return new DashboardInboxCheckService(
            dbContext,
            emailSyncService,
            emailAnalysisMaintenanceService,
            emailIntelligenceService,
            reminderEvidenceRefreshService,
            new DashboardQueryService(dbContext, clock),
            clock);
    }

    private sealed class CountingInboxEmailSource : IInboxEmailSource
    {
        private readonly IReadOnlyList<ExternalInboxEmail> _emails;

        public CountingInboxEmailSource(IReadOnlyList<ExternalInboxEmail> emails)
        {
            _emails = emails;
        }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
            InboxAccount inboxAccount,
            IReadOnlySet<string>? existingMessageIds = null,
            IReadOnlySet<string>? existingThreadIds = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_emails);
        }
    }
}
