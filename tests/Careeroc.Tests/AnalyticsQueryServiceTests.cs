using Careeroc.Application;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class AnalyticsQueryServiceTests
{
    private static readonly TestClock AnalyticsClock = new(new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GetAnalyticsAsync_ReturnsExpectedMetrics()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        var openAi = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "OpenAI",
            Role = "Backend Engineer",
            Status = ApplicationStatus.Applied,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero)
        };

        var figma = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Figma",
            Role = "Product Engineer",
            Status = ApplicationStatus.Interviewing,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 4, 9, 0, 0, TimeSpan.Zero)
        };

        var vercel = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Vercel",
            Role = "Staff Product Engineer",
            Status = ApplicationStatus.Offer,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero)
        };

        var notion = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Notion",
            Role = "Product Engineer",
            Status = ApplicationStatus.Rejected,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 3, 15, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 4, 15, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.AddRange(openAi, figma, vercel, notion);
        dbContext.JobApplicationEvents.AddRange(
            new JobApplicationEvent
            {
                JobApplicationId = figma.Id,
                JobApplication = figma,
                EventType = "interview_invitation",
                EventTimeUtc = figma.CreatedAtUtc.AddHours(48)
            },
            new JobApplicationEvent
            {
                JobApplicationId = vercel.Id,
                JobApplication = vercel,
                EventType = "offer",
                EventTimeUtc = vercel.CreatedAtUtc.AddHours(96)
            },
            new JobApplicationEvent
            {
                JobApplicationId = notion.Id,
                JobApplication = notion,
                EventType = "rejection",
                EventTimeUtc = notion.CreatedAtUtc.AddHours(24)
            });

        await dbContext.SaveChangesAsync();

        var service = new AnalyticsQueryService(dbContext, AnalyticsClock);

        var response = await service.GetAnalyticsAsync(userEmail: ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(4, response.Metrics.ApplicationsCount);
        Assert.Equal(4, response.Metrics.UniqueCompanies);
        Assert.Equal(2, response.Metrics.RolesCount);
        Assert.Equal(75.0m, response.Metrics.ReplyRate);
        Assert.Equal(50.0m, response.Metrics.InterviewRate);
        Assert.Equal(25.0m, response.Metrics.OfferRate);
        Assert.Equal(56.0m, response.Metrics.AvgTimeToFirstResponseHours);

        var vercelCompany = Assert.Single(response.CompanyBreakdown, item => item.Label == "Vercel");
        Assert.Equal(1, vercelCompany.ApplicationsCount);
        Assert.Equal(100.0m, vercelCompany.ReplyRate);
        Assert.Equal(100.0m, vercelCompany.InterviewRate);
        Assert.Equal(100.0m, vercelCompany.OfferRate);
        Assert.Equal(96.0m, vercelCompany.AvgTimeToFirstResponseHours);

        var productEngineerRole = Assert.Single(response.RoleBreakdown, item => item.Label == "Software Engineering");
        Assert.Equal(3, productEngineerRole.ApplicationsCount);
        Assert.Equal(100.0m, productEngineerRole.ReplyRate);
        Assert.Equal(66.7m, productEngineerRole.InterviewRate);
        Assert.Equal(33.3m, productEngineerRole.OfferRate);
        Assert.Equal(56.0m, productEngineerRole.AvgTimeToFirstResponseHours);
    }

    [Fact]
    public async Task GetAnalyticsAsync_ReturnsEmptyMetrics_WhenNoTrackedApplicationsExist()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        dbContext.Users.Add(
            new User
            {
                Email = ApplicationDefaults.DefaultUserEmail,
                Name = ApplicationDefaults.DefaultUserName
            });

        await dbContext.SaveChangesAsync();

        var service = new AnalyticsQueryService(dbContext, AnalyticsClock);
        var response = await service.GetAnalyticsAsync(userEmail: ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(0, response.Metrics.ApplicationsCount);
        Assert.Equal(0, response.Metrics.UniqueCompanies);
        Assert.Equal(0, response.Metrics.RolesCount);
        Assert.Equal(0, response.Metrics.ReplyRate);
        Assert.Equal(0, response.Metrics.InterviewRate);
        Assert.Equal(0, response.Metrics.OfferRate);
        Assert.Null(response.Metrics.AvgTimeToFirstResponseHours);
        Assert.Empty(response.CompanyBreakdown);
        Assert.Empty(response.RoleBreakdown);
    }

    [Fact]
    public async Task GetAnalyticsAsync_GroupsCompanyAndRoleBreakdowns_CaseInsensitively()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        var openAiOne = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "OpenAI",
            Role = "Backend Engineer",
            Status = ApplicationStatus.Interviewing,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero)
        };

        var openAiTwo = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "openai",
            Role = "backend engineer",
            Status = ApplicationStatus.Applied,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 3, 10, 0, 0, TimeSpan.Zero),
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 3, 10, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.AddRange(openAiOne, openAiTwo);
        dbContext.JobApplicationEvents.Add(
            new JobApplicationEvent
            {
                JobApplicationId = openAiTwo.Id,
                JobApplication = openAiTwo,
                EventType = "recruiter_outreach",
                EventTimeUtc = openAiTwo.CreatedAtUtc.AddHours(12)
            });

        await dbContext.SaveChangesAsync();

        var service = new AnalyticsQueryService(dbContext, AnalyticsClock);
        var response = await service.GetAnalyticsAsync(userEmail: ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(1, response.Metrics.UniqueCompanies);
        Assert.Equal(1, response.Metrics.RolesCount);

        var company = Assert.Single(response.CompanyBreakdown);
        Assert.Equal("OpenAI", company.Label);
        Assert.Equal(2, company.ApplicationsCount);

        var role = Assert.Single(response.RoleBreakdown);
        Assert.Equal("Software Engineering / Backend", role.Label);
        Assert.Equal(2, role.ApplicationsCount);
    }

    [Fact]
    public async Task GetAnalyticsAsync_CanonicalizesAtsAndDepartmentCompanyNames()
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
            EmailAddress = "candidate@gmail.com"
        };

        var myWorkday = CreateApplication(user, "Myworkday", "Software Engineer");
        var pinewood = CreateApplication(user, "Do-Not-Reply Pinewood", "AI Intern");
        var corvane = CreateApplication(user, "Corvane Systems Human Resources", "Data Science Intern");
        var qvlPeopleOps = CreateApplication(user, "QVL People Operations", "Software Engineer Intern");
        var qvl = CreateApplication(user, "QVL", "Data Analyst Intern");
        var withUs = CreateApplication(user, "Us", "Compiler Intern");

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(gmailInbox);
        dbContext.JobApplications.AddRange(myWorkday, pinewood, corvane, qvlPeopleOps, qvl, withUs);
        dbContext.InboxEmails.AddRange(
            CreateEmail(gmailInbox, myWorkday, "brx@myworkday.com", "Thank you for applying to BRX!"),
            CreateEmail(gmailInbox, pinewood, "do-not-reply pinewood <pinewood@myworkday.com>", "Thanks for Applying to Pinewood"),
            CreateEmail(gmailInbox, corvane, "Corvane Systems Human Resources <corvane@myworkday.com>", "Thank you for applying! | R1000001 Software Developer Intern role"),
            CreateEmail(gmailInbox, qvlPeopleOps, "QVL People Operations <qvl@myworkday.com>", "Internship - Software Engineer at QVL: Thank you for applying"),
            CreateEmail(gmailInbox, qvl, "QVL People Operations <qvl@myworkday.com>", "Update on your application: Internship - Data Analyst at QVL"),
            CreateEmail(gmailInbox, withUs, "harbor@myworkday.com", "Brightline Compute: Thank you for applying to the Intern - Compiler with us!"));

        await dbContext.SaveChangesAsync();

        var service = new AnalyticsQueryService(dbContext, AnalyticsClock);
        var response = await service.GetAnalyticsAsync(userEmail: ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(5, response.Metrics.UniqueCompanies);
        Assert.DoesNotContain(response.CompanyBreakdown, item => item.Label is "Myworkday" or "Do-Not-Reply Pinewood" or "Corvane Systems Human Resources" or "QVL People Operations" or "Us");
        Assert.Contains(response.CompanyBreakdown, item => item.Label == "BRX");
        Assert.Contains(response.CompanyBreakdown, item => item.Label == "Pinewood");
        Assert.Contains(response.CompanyBreakdown, item => item.Label == "Corvane Systems");
        Assert.Contains(response.CompanyBreakdown, item => item.Label == "Brightline Compute");

        var qvlBreakdown = Assert.Single(response.CompanyBreakdown, item => item.Label == "QVL");
        Assert.Equal(2, qvlBreakdown.ApplicationsCount);
    }

    [Fact]
    public async Task GetAnalyticsAsync_FiltersByLastThirtyDaysUsingApplicationCreatedAt()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.AddRange(
            new JobApplication
            {
                UserId = user.Id,
                User = user,
                Company = "Recent Co",
                Role = "Engineer",
                Status = ApplicationStatus.Applied,
                CreatedAtUtc = AnalyticsClock.UtcNow.AddDays(-12),
                LastActivityAtUtc = AnalyticsClock.UtcNow.AddDays(-12)
            },
            new JobApplication
            {
                UserId = user.Id,
                User = user,
                Company = "Older Co",
                Role = "Designer",
                Status = ApplicationStatus.Applied,
                CreatedAtUtc = AnalyticsClock.UtcNow.AddDays(-61),
                LastActivityAtUtc = AnalyticsClock.UtcNow.AddDays(-61)
            });

        await dbContext.SaveChangesAsync();

        var service = new AnalyticsQueryService(dbContext, AnalyticsClock);
        var response = await service.GetAnalyticsAsync(
            AnalyticsQueryService.Last30DaysRange,
            userEmail: ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(1, response.Metrics.ApplicationsCount);
        Assert.Equal("Recent Co", Assert.Single(response.CompanyBreakdown).Label);
    }

    [Fact]
    public async Task GetAnalyticsAsync_FiltersByInboxProvider()
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

        var customInbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.DemoInboxProvider,
            EmailAddress = "demo@example.com"
        };

        var gmailApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Google",
            Role = "Software Engineer",
            Status = ApplicationStatus.Interviewing,
            CreatedAtUtc = AnalyticsClock.UtcNow.AddDays(-10),
            LastActivityAtUtc = AnalyticsClock.UtcNow.AddDays(-4)
        };

        var demoApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Linear",
            Role = "Product Engineer",
            Status = ApplicationStatus.Offer,
            CreatedAtUtc = AnalyticsClock.UtcNow.AddDays(-15),
            LastActivityAtUtc = AnalyticsClock.UtcNow.AddDays(-2)
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.AddRange(gmailInbox, customInbox);
        dbContext.JobApplications.AddRange(gmailApplication, demoApplication);
        dbContext.InboxEmails.AddRange(
            new InboxEmail
            {
                InboxAccountId = gmailInbox.Id,
                InboxAccount = gmailInbox,
                JobApplicationId = gmailApplication.Id,
                JobApplication = gmailApplication,
                ExternalMessageId = "gmail-1",
                ThreadId = "gmail-thread",
                Sender = "recruiting@google.com",
                Subject = "Interview invitation",
                ParsedBody = "Let's schedule an interview.",
                RawBody = "Let's schedule an interview.",
                ReceivedAtUtc = AnalyticsClock.UtcNow.AddDays(-4)
            },
            new InboxEmail
            {
                InboxAccountId = customInbox.Id,
                InboxAccount = customInbox,
                JobApplicationId = demoApplication.Id,
                JobApplication = demoApplication,
                ExternalMessageId = "custom-1",
                ThreadId = "custom-thread",
                Sender = "talent@linear.app",
                Subject = "Offer",
                ParsedBody = "We are excited to extend an offer.",
                RawBody = "We are excited to extend an offer.",
                ReceivedAtUtc = AnalyticsClock.UtcNow.AddDays(-2)
            });

        await dbContext.SaveChangesAsync();

        var service = new AnalyticsQueryService(dbContext, AnalyticsClock);
        var response = await service.GetAnalyticsAsync(
            provider: ApplicationDefaults.DemoInboxProvider,
            userEmail: ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(1, response.Metrics.ApplicationsCount);
        Assert.Equal("Linear", Assert.Single(response.CompanyBreakdown).Label);
        Assert.Equal(100.0m, response.Metrics.OfferRate);
    }

    private static JobApplication CreateApplication(User user, string company, string role)
    {
        return new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = company,
            Role = role,
            Status = ApplicationStatus.Applied,
            CreatedAtUtc = AnalyticsClock.UtcNow.AddDays(-3),
            LastActivityAtUtc = AnalyticsClock.UtcNow.AddDays(-3)
        };
    }

    private static InboxEmail CreateEmail(
        InboxAccount inboxAccount,
        JobApplication application,
        string sender,
        string subject)
    {
        return new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = Guid.NewGuid().ToString("N"),
            ThreadId = Guid.NewGuid().ToString("N"),
            Sender = sender,
            Subject = subject,
            RawBody = subject,
            ParsedBody = subject,
            ReceivedAtUtc = AnalyticsClock.UtcNow.AddDays(-2)
        };
    }
}
