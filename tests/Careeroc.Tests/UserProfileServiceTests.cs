using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Enums;
using Careeroc.Domain.Entities;

namespace Careeroc.Tests;

public sealed class UserProfileServiceTests
{
    [Fact]
    public async Task GetProfileAsync_CreatesProfile_WithDefaultTimeZone()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var service = new UserProfileService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var profile = await service.GetProfileAsync("student@example.com", "Student User");

        Assert.Equal("student@example.com", profile.Email);
        Assert.Equal("Student User", profile.Name);
        Assert.Equal(ApplicationDefaults.DefaultTimeZoneId, profile.TimeZoneId);
        Assert.Equal("inbox-blue", profile.ProfileIcon);
        Assert.Equal(20, profile.Evaluation.ReadinessScore);
        Assert.Contains("target role", profile.Evaluation.NextActions[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateProfileAsync_StoresCandidateContext()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new UserProfileService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var profile = await service.UpdateProfileAsync(
            new UpdateUserProfileRequest(
                "Jordan Lee",
                "Asia/Shanghai",
                "Shanghai",
                "Software engineering intern",
                "resume-blue"),
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName);

        Assert.Equal("Jordan Lee", profile.Name);
        Assert.Equal("Asia/Shanghai", profile.TimeZoneId);
        Assert.Equal("resume-blue", profile.ProfileIcon);
        Assert.Equal("Shanghai", profile.Location);
        Assert.Equal("Software engineering intern", profile.TargetRole);
        Assert.True(profile.Evaluation.ReadinessScore >= 45);
        Assert.Contains(profile.Evaluation.Strengths, strength => strength.Contains("Target role", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DismissWelcomeAsync_PersistsOncePerUser()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var now = new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var service = new UserProfileService(dbContext, new TestClock(now));
        var profile = await service.DismissWelcomeAsync("student@example.com", "Student User");

        Assert.Equal(now, profile.WelcomeDismissedAtUtc);

        var secondProfile = await service.DismissWelcomeAsync("student@example.com", "Student User");
        var otherProfile = await service.GetProfileAsync("other@example.com", "Other User");

        Assert.Equal(now, secondProfile.WelcomeDismissedAtUtc);
        Assert.Null(otherProfile.WelcomeDismissedAtUtc);
    }

    [Fact]
    public async Task UpdateProfileAsync_RejectsUnsupportedProfileIcon()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var service = new UserProfileService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateProfileAsync(
                new UpdateUserProfileRequest(
                    "Student User",
                    "America/Los_Angeles",
                    null,
                    null,
                    "giant-logo"),
                "student@example.com",
                "Student User"));
    }

    [Fact]
    public async Task UpdateProfileAsync_RejectsUnsupportedTimeZone()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var service = new UserProfileService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateProfileAsync(
                new UpdateUserProfileRequest(
                    "Student User",
                    "Mars/Olympus",
                    null,
                    null),
                "student@example.com",
                "Student User"));
    }

    [Fact]
    public async Task GetProfileAsync_BuildsEvaluationFromMailboxApplicationsAndMemory()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName,
            Location = "Portland, OR",
            TargetRole = "Software engineering intern"
        };
        var inbox = new InboxAccount
        {
            User = user,
            UserId = user.Id,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "student@gmail.com"
        };
        InboxAccountMetadataJson.Write(
            inbox,
            new InboxAccountMetadata
            {
                ConnectionStatus = "Connected"
            });
        var application = new JobApplication
        {
            User = user,
            UserId = user.Id,
            Company = "Roblox",
            Role = "Software Engineering Intern",
            Status = ApplicationStatus.Interviewing,
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 25, 10, 0, 0, TimeSpan.Zero)
        };
        var memory = new UserMemoryEntry
        {
            User = user,
            UserId = user.Id,
            Kind = "preference",
            Key = "target_stack",
            Value = "full-stack TypeScript",
            ConfirmationStatus = "confirmed"
        };
        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inbox);
        dbContext.JobApplications.Add(application);
        dbContext.UserMemoryEntries.Add(memory);
        await dbContext.SaveChangesAsync();

        var service = new UserProfileService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var profile = await service.GetProfileAsync(
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName);

        Assert.True(profile.Evaluation.ReadinessScore >= 80);
        Assert.Equal(1, profile.Evaluation.Evidence.ConnectedInboxes);
        Assert.Equal(1, profile.Evaluation.Evidence.TotalApplications);
        Assert.Equal(1, profile.Evaluation.Evidence.InterviewingApplications);
        Assert.Equal(1, profile.Evaluation.Evidence.MemoryEntries);
        var profileMemory = Assert.Single(profile.Memory);
        Assert.Equal("target_stack", profileMemory.Key);
        Assert.Equal("full-stack TypeScript", profileMemory.Value);
        Assert.Contains(profile.Evaluation.Strengths, strength => strength.Contains("high-signal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetProfileAsync_UsesResumeAndMailboxBridgeForEvaluation()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName,
            Location = "Portland, OR",
            TargetRole = "Machine Learning Engineer"
        };
        var resumeDocument = new ResumeDocument
        {
            User = user,
            UserId = user.Id,
            Title = "Spring resume"
        };
        var resumeVersion = new ResumeVersion
        {
            ResumeDocument = resumeDocument,
            ResumeDocumentId = resumeDocument.Id,
            Summary = "Resume summary",
            Evaluation = new ResumeEvaluation
            {
                Score = 85,
                Verdict = "Strong resume foundation.",
                StrengthsJson = "[]",
                GapsJson = "[]",
                NextActionsJson = "[]",
                EvidenceJson = "{}"
            }
        };
        resumeVersion.Evaluation.ResumeVersion = resumeVersion;
        resumeVersion.Evaluation.ResumeVersionId = resumeVersion.Id;
        resumeDocument.Versions.Add(resumeVersion);
        dbContext.Users.Add(user);
        dbContext.ResumeDocuments.Add(resumeDocument);
        dbContext.JobApplications.Add(
            new JobApplication
            {
                User = user,
                UserId = user.Id,
                Company = "Roblox",
                Role = "Machine Learning Engineer",
                Status = ApplicationStatus.Applied
            });
        dbContext.UserMemoryEntries.Add(
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "orchestrator",
                Key = "orchestrator.resume_mailbox_bridge",
                Value = "{}",
                Summary = "Resume evidence and mailbox outcomes are beginning to align.",
                SourceType = "career_orchestrator",
                ConfirmationStatus = "confirmed"
            });
        await dbContext.SaveChangesAsync();

        var service = new UserProfileService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));
        var profile = await service.GetProfileAsync(
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName);

        Assert.Equal(1, profile.Evaluation.Evidence.ResumeVersions);
        Assert.Equal(85, profile.Evaluation.Evidence.LatestResumeScore);
        Assert.Equal("Spring resume", profile.Evaluation.Evidence.LatestResumeTitle);
        Assert.Equal(
            "Resume evidence and mailbox outcomes are beginning to align.",
            profile.Evaluation.Summary);
        Assert.Contains(profile.Evaluation.Strengths, strength => strength.Contains("resume", StringComparison.OrdinalIgnoreCase));
    }
}
