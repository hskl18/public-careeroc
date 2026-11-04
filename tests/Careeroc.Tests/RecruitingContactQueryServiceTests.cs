using Careeroc.Application.Services;
using Careeroc.Contracts.Contacts;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class RecruitingContactQueryServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsContactHistoryWithoutRawEmailBodies()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = "student@example.com",
            Name = "Student"
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
            Company = "OpenAI",
            Role = "Software Engineer",
            Source = "Email",
            Status = ApplicationStatus.Interviewing
        };
        var contact = new RecruitingContact
        {
            UserId = user.Id,
            User = user,
            Email = "recruiter@example.com",
            DisplayName = "Riley Recruiter",
            CompanyHint = "OpenAI",
            RoleHint = "Recruiter",
            ContactType = RecruitingContactType.Recruiter,
            Confidence = 0.92m,
            MessageCount = 1,
            FirstSeenAtUtc = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            LastSeenAtUtc = new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero)
        };
        var email = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            RecruitingContactId = contact.Id,
            RecruitingContact = contact,
            ExternalMessageId = "gmail-1",
            ThreadId = "thread-1",
            Sender = "Riley Recruiter <recruiter@example.com>",
            Subject = "Interview details",
            RawBody = "secret raw body",
            ParsedBody = "secret parsed body",
            ReceivedAtUtc = contact.LastSeenAtUtc
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.RecruitingContacts.Add(contact);
        dbContext.InboxEmails.Add(email);
        await dbContext.SaveChangesAsync();

        var service = new RecruitingContactQueryService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero)));

        var response = await service.GetAsync(user.Email, 10);

        var dto = Assert.Single(response.Contacts);
        Assert.Equal(contact.Email, dto.Email);
        Assert.Equal("Recruiter", dto.ContactType);
        var recentEmail = Assert.Single(dto.RecentEmails);
        Assert.Equal("Interview details", recentEmail.Subject);
        Assert.Equal(application.Id, recentEmail.JobApplicationId);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesOnlyOwnedContactAliasFields()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = "student@example.com",
            Name = "Student"
        };
        var otherUser = new User
        {
            Email = "other@example.com",
            Name = "Other"
        };
        var contact = new RecruitingContact
        {
            UserId = user.Id,
            User = user,
            Email = "recruiter@example.com",
            DisplayName = "Riley",
            ContactType = RecruitingContactType.Recruiter,
            Confidence = 0.80m,
            MessageCount = 3,
            FirstSeenAtUtc = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            LastSeenAtUtc = new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero)
        };

        dbContext.Users.AddRange(user, otherUser);
        dbContext.RecruitingContacts.Add(contact);
        await dbContext.SaveChangesAsync();

        var service = new RecruitingContactQueryService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero)));

        var blocked = await service.UpdateAsync(
            otherUser.Email,
            contact.Id,
            new UpdateRecruitingContactRequest("Wrong", "Wrong Co", "Wrong Role", "Coordinator"));
        var updated = await service.UpdateAsync(
            user.Email,
            contact.Id,
            new UpdateRecruitingContactRequest(
                "Riley Chen",
                "Northstar",
                "Recruiting Coordinator",
                "Hiring Manager"));

        Assert.Null(blocked);
        Assert.NotNull(updated);
        Assert.Equal("Riley Chen", updated.DisplayName);
        Assert.Equal("Northstar", updated.CompanyHint);
        Assert.Equal("Recruiting Coordinator", updated.RoleHint);
        Assert.Equal("HiringManager", updated.ContactType);

        var stored = await dbContext.RecruitingContacts.FindAsync(contact.Id);
        Assert.NotNull(stored);
        Assert.Equal(user.Id, stored.UserId);
        Assert.Equal(RecruitingContactType.HiringManager, stored.ContactType);
    }

    [Fact]
    public async Task UpdateAsync_KeepsFieldsTheClientLeavesOutAndClearsEmptyOnes()
    {
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var user = new User { Email = "student@example.com", Name = "Student" };
        var contact = new RecruitingContact
        {
            UserId = user.Id,
            User = user,
            Email = "recruiter@example.com",
            DisplayName = "Riley",
            CompanyHint = "Northstar",
            RoleHint = "Recruiter",
            ContactType = RecruitingContactType.Recruiter
        };
        dbContext.Users.Add(user);
        dbContext.RecruitingContacts.Add(contact);
        await dbContext.SaveChangesAsync();
        var service = new RecruitingContactQueryService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero)));

        var updated = await service.UpdateAsync(
            user.Email,
            contact.Id,
            new UpdateRecruitingContactRequest(null, "Kestrel AI", "", null));

        Assert.Equal("Riley", updated!.DisplayName);
        Assert.Equal("Kestrel AI", updated.CompanyHint);
        Assert.Null(updated.RoleHint);
        Assert.Equal("Recruiter", updated.ContactType);
    }

    [Fact]
    public async Task UpdateAsync_MergesContact_WhenCorrectedEmailAlreadyExists()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = "student@example.com",
            Name = "Student"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = "gmail",
            EmailAddress = user.Email
        };
        var canonicalContact = new RecruitingContact
        {
            UserId = user.Id,
            User = user,
            Email = "recruiting@example.com",
            DisplayName = "Recruiting Team",
            MessageCount = 1,
            FirstSeenAtUtc = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            LastSeenAtUtc = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero)
        };
        var duplicateContact = new RecruitingContact
        {
            UserId = user.Id,
            User = user,
            Email = "alias@example.com",
            DisplayName = "Shared Alias",
            CompanyHint = "Northstar",
            ContactType = RecruitingContactType.Coordinator,
            Confidence = 0.88m,
            MessageCount = 1,
            FirstSeenAtUtc = new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero),
            LastSeenAtUtc = new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero)
        };
        var duplicateEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            RecruitingContactId = duplicateContact.Id,
            RecruitingContact = duplicateContact,
            ExternalMessageId = "gmail-1",
            ThreadId = "thread-1",
            Sender = "Shared Alias <alias@example.com>",
            Subject = "Scheduling",
            ReceivedAtUtc = duplicateContact.LastSeenAtUtc
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.RecruitingContacts.AddRange(canonicalContact, duplicateContact);
        dbContext.InboxEmails.Add(duplicateEmail);
        await dbContext.SaveChangesAsync();

        var service = new RecruitingContactQueryService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero)));

        var updated = await service.UpdateAsync(
            user.Email,
            duplicateContact.Id,
            new UpdateRecruitingContactRequest(null, null, null, null, canonicalContact.Email));

        Assert.NotNull(updated);
        Assert.Equal(canonicalContact.Id, updated.Id);
        Assert.Equal(2, updated.MessageCount);
        Assert.Equal("Northstar", updated.CompanyHint);
        Assert.Equal("Coordinator", updated.ContactType);
        Assert.Equal(0.88m, updated.Confidence);
        Assert.Equal(canonicalContact.Id, duplicateEmail.RecruitingContactId);
        Assert.DoesNotContain(dbContext.RecruitingContacts, contact => contact.Id == duplicateContact.Id);
    }
}
