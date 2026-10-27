using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Application.Services.Agents;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class BackendProductTodoTests
{
    private static readonly TestClock Clock = new(new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void InboxTriageAgentService_ShortcutsRoutineVerificationMail()
    {
        var decision = new InboxTriageAgentService().Triage(
            new InboxEmail
            {
                Sender = "noreply@google.com",
                Subject = "Your verification code",
                ParsedBody = "Your verification code is 123456."
            });

        Assert.False(decision.IsRecruitingRelated);
        Assert.Equal("google_verification", decision.ShortcutRuleId);
    }

    [Fact]
    public void InboxTriageAgentService_KeepsAmbiguousRecruiterMailInExtractionPath()
    {
        var decision = new InboxTriageAgentService().Triage(
            new InboxEmail
            {
                Sender = "jane.recruiter@example.com",
                Subject = "Next steps for your Software Engineer interview",
                ParsedBody = "Thanks for applying. Please share availability for a recruiter screen this week."
            });

        Assert.True(decision.IsRecruitingRelated);
        Assert.Null(decision.ShortcutRuleId);
    }

    [Fact]
    public async Task ApplicationLookupQueryService_SortsByUrgency()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var urgent = new JobApplication
        {
            User = user,
            UserId = user.Id,
            Company = "AssessCo",
            Role = "Software Engineer",
            Status = ApplicationStatus.OaPending,
            LastActivityAtUtc = Clock.UtcNow.AddDays(-1),
            CreatedAtUtc = Clock.UtcNow.AddDays(-2)
        };
        var rejected = new JobApplication
        {
            User = user,
            UserId = user.Id,
            Company = "OldCo",
            Role = "Software Engineer",
            Status = ApplicationStatus.Rejected,
            LastActivityAtUtc = Clock.UtcNow,
            CreatedAtUtc = Clock.UtcNow
        };
        dbContext.Users.Add(user);
        dbContext.JobApplications.AddRange(urgent, rejected);
        dbContext.Reminders.Add(
            new Reminder
            {
                JobApplication = urgent,
                JobApplicationId = urgent.Id,
                Title = "Complete assessment",
                Type = ReminderType.AssessmentDeadline,
                Status = ReminderStatus.Open,
                DueAtUtc = Clock.UtcNow.AddHours(3)
            });
        await dbContext.SaveChangesAsync();

        var response = await new ApplicationLookupQueryService(dbContext, Clock)
            .SearchAsync(ApplicationDefaults.DefaultUserEmail, null, 10, "urgency");

        Assert.Equal(urgent.Id, response.Items[0].Id);
        Assert.True(response.Items[0].UrgencyScore > response.Items[1].UrgencyScore);
        Assert.Equal(Clock.UtcNow.AddHours(3), response.Items[0].NextActionDueAtUtc);
    }

    [Fact]
    public async Task DashboardQueryService_UsesPersistedGmailProfileTotalForBackfillEstimate()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var inboxAccount = new InboxAccount
        {
            User = user,
            UserId = user.Id,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "student@example.com",
            CreatedAtUtc = Clock.UtcNow.AddMinutes(-10)
        };
        InboxAccountMetadataJson.Write(
            inboxAccount,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                GmailMessagesTotal = 1042,
                GmailProfileObservedAtUtc = Clock.UtcNow
            });
        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.InboxEmails.Add(
            new InboxEmail
            {
                InboxAccount = inboxAccount,
                InboxAccountId = inboxAccount.Id,
                ExternalMessageId = "msg-1",
                ThreadId = "thread-1",
                Sender = "recruiting@example.com",
                Subject = "Interview",
                ParsedBody = "Interview details",
                RawBody = "Interview details",
                ReceivedAtUtc = Clock.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var dashboard = await new DashboardQueryService(dbContext, Clock)
            .GetDashboardAsync(user.Email, CancellationToken.None);

        Assert.Equal(1, dashboard.Inbox.ScannedCount);
        Assert.Equal(1042, dashboard.Inbox.TotalEstimate);
    }

    [Fact]
    public async Task ResumeFitService_ReturnsDeterministicSkillOverlap()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = SeedUser(dbContext);
        var application = new JobApplication
        {
            User = user,
            UserId = user.Id,
            Company = "CloudCo",
            Role = "Backend TypeScript AWS Engineer",
            Status = ApplicationStatus.Applied
        };
        var document = SeedResume(user, "Built TypeScript APIs on AWS with PostgreSQL.");
        dbContext.JobApplications.Add(application);
        dbContext.ResumeDocuments.Add(document);
        await dbContext.SaveChangesAsync();

        var fit = await new ResumeFitService(dbContext, Clock).GetFitAsync(
            application.Id,
            user.Email);

        Assert.NotNull(fit);
        Assert.True(fit.MatchScore > 50);
        Assert.Contains(fit.Strengths, strength => strength.Contains("typescript", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResumeSuggestionService_AcceptCreatesNewVersionAndCorrection()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = SeedUser(dbContext);
        var document = SeedResume(user, "Contact: Not detected");
        dbContext.ResumeDocuments.Add(document);
        await dbContext.SaveChangesAsync();

        var service = new ResumeSuggestionService(dbContext, Clock);
        var suggestions = await service.GetSuggestionsAsync(document.Id, user.Email);
        var suggestion = Assert.Single(suggestions!);

        var accepted = await service.AcceptSuggestionAsync(document.Id, suggestion.Id, user.Email);

        Assert.True(accepted);
        Assert.Equal(2, dbContext.ResumeVersions.Count(version => version.ResumeDocumentId == document.Id));
        Assert.Single(dbContext.ResumeCorrections);
    }

    private static User SeedUser(Careeroc.Infrastructure.Persistence.CareerocDbContext dbContext)
    {
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        dbContext.Users.Add(user);
        return user;
    }

    private static ResumeDocument SeedResume(User user, string text)
    {
        var document = new ResumeDocument
        {
            User = user,
            UserId = user.Id,
            Title = "Test Resume",
            SourceKind = "text",
            SourceText = text,
            CreatedAtUtc = Clock.UtcNow,
            UpdatedAtUtc = Clock.UtcNow
        };
        document.Versions.Add(
            new ResumeVersion
            {
                ResumeDocument = document,
                ResumeDocumentId = document.Id,
                Summary = text,
                SkillsJson = "[\"typescript\",\"aws\",\"postgres\"]",
                ProjectHighlightsJson = "[]",
                StructuredDataJson = "{\"contact\":{\"name\":\"Not detected\"},\"skills\":{\"technical\":[\"typescript\",\"aws\",\"postgres\"]}}",
                ExtractionMetadataJson = "{}",
                ExperienceCount = 1,
                ProjectCount = 1,
                CreatedAtUtc = Clock.UtcNow
            });
        return document;
    }
}
