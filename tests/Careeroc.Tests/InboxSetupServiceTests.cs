using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Contracts.Inbox;
using Careeroc.Domain.Entities;

namespace Careeroc.Tests;

public sealed class InboxSetupServiceTests
{
    [Fact]
    public async Task RegisterGmailAsync_CreatesGmailAccount()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = CreateService(dbContext, clock);

        var response = await service.RegisterGmailAsync(
            new RegisterInboxAccountRequest
            {
                EmailAddress = "alex.personal@example.com"
            },
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName);

        var account = Assert.Single(dbContext.InboxAccounts);
        Assert.Equal(ApplicationDefaults.GmailInboxProvider, account.Provider);
        Assert.Equal("alex.personal@example.com", account.EmailAddress);

        var dto = Assert.Single(response.Accounts);
        Assert.Equal("Gmail", dto.ProviderLabel);
        Assert.Equal("oauth", dto.SetupMode);
        Assert.Equal("Ready for OAuth", dto.ConnectionStatus);
        Assert.Equal(3, dto.BackfillMonths);
        Assert.False(dto.BackfillAllHistory);
    }

    [Fact]
    public async Task StartGmailOAuthAsync_SavesPendingSessionAndReturnsAuthorizationUrl()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient();

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = CreateService(dbContext, clock, gmailClient);

        var authorizationUrl = await service.StartGmailOAuthAsync(
            new RegisterInboxAccountRequest
            {
                EmailAddress = "alex.personal@example.com"
            },
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            "/",
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName,
            CancellationToken.None);

        var account = Assert.Single(dbContext.InboxAccounts);
        var metadata = InboxAccountMetadataJson.Read(account, _ => new InboxAccountMetadata());

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", authorizationUrl, StringComparison.Ordinal);
        Assert.Equal("Waiting for Google", metadata.ConnectionStatus);
        Assert.Equal(3, metadata.BackfillMonths);
        Assert.False(metadata.BackfillAllHistory);
        Assert.False(string.IsNullOrWhiteSpace(metadata.OAuthState));
        Assert.False(string.IsNullOrWhiteSpace(metadata.OAuthCodeVerifier));
        Assert.Equal("/", metadata.OAuthReturnUrl);
        Assert.Equal(clock.UtcNow.AddMinutes(15), metadata.OAuthStateExpiresAtUtc);
        Assert.Equal("alex.personal@example.com", gmailClient.LastLoginHint);
    }

    [Fact]
    public async Task StartGmailOAuthAsync_CreatesGmailAccountAlongsideExistingInbox()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient();

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        user.InboxAccounts.Add(
            new InboxAccount
            {
                Provider = ApplicationDefaults.DemoInboxProvider,
                EmailAddress = ApplicationDefaults.DefaultUserEmail
            });

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock, gmailClient);

        await service.StartGmailOAuthAsync(
            new RegisterInboxAccountRequest
            {
                EmailAddress = "alex.personal@example.com"
            },
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            "/",
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName,
            CancellationToken.None);

        Assert.Equal(2, dbContext.InboxAccounts.Count());
        var gmailAccount = Assert.Single(
            dbContext.InboxAccounts.Where(account => account.Provider == ApplicationDefaults.GmailInboxProvider));
        var metadata = InboxAccountMetadataJson.Read(gmailAccount, _ => new InboxAccountMetadata());

        Assert.Equal("alex.personal@example.com", gmailAccount.EmailAddress);
        Assert.Equal("Waiting for Google", metadata.ConnectionStatus);
        Assert.Equal(3, metadata.BackfillMonths);
    }

    [Fact]
    public async Task CompleteGmailOAuthAsync_StoresTokensAndMarksAccountConnected()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient
        {
            ExchangeResult = new GmailAuthorizationResult(
                "access-token",
                "refresh-token",
                clock.UtcNow.AddHours(1)),
            Profile = new GmailProfile("alex.personal@example.com", "history-123")
        };

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = CreateService(dbContext, clock, gmailClient);

        await service.StartGmailOAuthAsync(
            new RegisterInboxAccountRequest
            {
                EmailAddress = "alex.personal@example.com"
            },
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            "/",
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName,
            CancellationToken.None);

        var pendingAccount = Assert.Single(dbContext.InboxAccounts);
        var pendingMetadata = InboxAccountMetadataJson.Read(pendingAccount, _ => new InboxAccountMetadata());

        var completion = await service.CompleteGmailOAuthAsync(
            pendingMetadata.OAuthState!,
            "auth-code",
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            ApplicationDefaults.DefaultUserEmail,
            CancellationToken.None);

        var connectedAccount = Assert.Single(dbContext.InboxAccounts);
        var connectedMetadata = InboxAccountMetadataJson.Read(connectedAccount, _ => new InboxAccountMetadata());

        Assert.True(completion.Succeeded);
        Assert.Equal("/", completion.ReturnUrl);
        Assert.Equal("Connected", connectedMetadata.ConnectionStatus);
        Assert.Equal("history-123", connectedMetadata.GmailHistoryId);
        Assert.Equal("access-token", connectedMetadata.AccessToken);
        Assert.Equal("refresh-token", connectedMetadata.RefreshToken);
        Assert.Equal(clock.UtcNow.AddHours(1), connectedMetadata.AccessTokenExpiresAtUtc);
        Assert.Equal(clock.UtcNow, connectedMetadata.ConnectedAtUtc);
        Assert.Equal(3, connectedMetadata.BackfillMonths);
        Assert.False(connectedMetadata.BackfillAllHistory);
        Assert.Null(connectedMetadata.OAuthState);
        Assert.Null(connectedMetadata.OAuthCodeVerifier);
        Assert.Null(connectedMetadata.OAuthReturnUrl);
        Assert.Null(connectedMetadata.OAuthStateExpiresAtUtc);
    }

    [Fact]
    public async Task CompleteGmailOAuthAsync_OnlyTheUserWhoStartedTheFlowCanFinishIt()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient
        {
            ExchangeResult = new GmailAuthorizationResult("access-token", "refresh-token", clock.UtcNow.AddHours(1)),
            Profile = new GmailProfile("victim.personal@example.com", "history-123")
        };
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var service = CreateService(dbContext, clock, gmailClient);
        await service.StartGmailOAuthAsync(
            new RegisterInboxAccountRequest { EmailAddress = "attacker.personal@example.com" },
            "http://localhost:3000/api/inbox-accounts/gmail/oauth/callback",
            "/",
            "attacker@example.com",
            "Attacker",
            CancellationToken.None);
        var state = InboxAccountMetadataJson.Read(Assert.Single(dbContext.InboxAccounts), _ => new InboxAccountMetadata()).OAuthState!;

        // The attacker forwards the Google consent link; the victim consents while signed in as themselves.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteGmailOAuthAsync(
                state,
                "victims-auth-code",
                "http://localhost:3000/api/inbox-accounts/gmail/oauth/callback",
                "victim@example.com",
                CancellationToken.None));

        Assert.Contains("No pending Gmail authorization session", exception.Message, StringComparison.Ordinal);
        Assert.Null(InboxAccountMetadataJson.Read(Assert.Single(dbContext.InboxAccounts), _ => new InboxAccountMetadata()).AccessToken);
    }

    [Theory]
    [InlineData("/app/inbox#mail-sync", true)]
    [InlineData("/", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("/\t/evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("app/inbox", false)]
    public void ReturnUrlsMustStayOnThisSite(string returnUrl, bool allowed)
    {
        Assert.Equal(allowed, ApplicationDefaults.IsLocalPath(returnUrl));
    }

    [Fact]
    public async Task CompleteGmailOAuthAsync_RejectsExpiredPendingSession()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var startedAt = new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero);
        var gmailClient = new FakeGmailInboxClient();

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var startService = CreateService(dbContext, new TestClock(startedAt), gmailClient);

        await startService.StartGmailOAuthAsync(
            new RegisterInboxAccountRequest
            {
                EmailAddress = "alex.personal@example.com"
            },
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            "/app/profile#mail-sync",
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName,
            CancellationToken.None);

        var pendingAccount = Assert.Single(dbContext.InboxAccounts);
        var pendingMetadata = InboxAccountMetadataJson.Read(pendingAccount, _ => new InboxAccountMetadata());
        var expiredService = CreateService(
            dbContext,
            new TestClock(startedAt.AddMinutes(16)),
            gmailClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => expiredService.CompleteGmailOAuthAsync(
                pendingMetadata.OAuthState!,
                "auth-code",
                "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
                ApplicationDefaults.DefaultUserEmail,
                CancellationToken.None));

        var expiredMetadata = InboxAccountMetadataJson.Read(pendingAccount, _ => new InboxAccountMetadata());

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("OAuth session expired", expiredMetadata.ConnectionStatus);
        Assert.Null(expiredMetadata.OAuthState);
        Assert.Null(expiredMetadata.OAuthCodeVerifier);
        Assert.Null(expiredMetadata.OAuthReturnUrl);
        Assert.Null(expiredMetadata.OAuthStateExpiresAtUtc);
    }

    [Fact]
    public async Task CompleteGmailOAuthAsync_DoesNotMergeWithAnotherUsersGmailAccount()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient
        {
            ExchangeResult = new GmailAuthorizationResult(
                "target-access-token",
                "target-refresh-token",
                clock.UtcNow.AddHours(1)),
            Profile = new GmailProfile("shared@gmail.com", "history-123")
        };

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var otherUser = new User
        {
            Email = "other@example.com",
            Name = "Other User"
        };
        var otherAccount = new InboxAccount
        {
            UserId = otherUser.Id,
            User = otherUser,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "shared@gmail.com"
        };
        InboxAccountMetadataJson.Write(
            otherAccount,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                AccessToken = "other-access-token",
                RefreshToken = "other-refresh-token"
            });

        dbContext.Users.Add(otherUser);
        dbContext.InboxAccounts.Add(otherAccount);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock, gmailClient);

        await service.StartGmailOAuthAsync(
            new RegisterInboxAccountRequest
            {
                EmailAddress = "target-login-hint@gmail.com"
            },
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            "/",
            userEmail: "target@example.com",
            userName: "Target User",
            cancellationToken: CancellationToken.None);

        var pendingAccount = dbContext.InboxAccounts.Single(
            account => account.EmailAddress == "target-login-hint@gmail.com");
        var pendingMetadata = InboxAccountMetadataJson.Read(pendingAccount, _ => new InboxAccountMetadata());

        var completion = await service.CompleteGmailOAuthAsync(
            pendingMetadata.OAuthState!,
            "auth-code",
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            "target@example.com",
            CancellationToken.None);

        Assert.True(completion.Succeeded);
        Assert.Equal(2, dbContext.InboxAccounts.Count());

        var targetAccount = dbContext.InboxAccounts.Single(
            account => account.User.Email == "target@example.com");
        var targetMetadata = InboxAccountMetadataJson.Read(targetAccount, _ => new InboxAccountMetadata());
        var otherMetadata = InboxAccountMetadataJson.Read(otherAccount, _ => new InboxAccountMetadata());

        Assert.Equal("shared@gmail.com", targetAccount.EmailAddress);
        Assert.Equal("target-access-token", targetMetadata.AccessToken);
        Assert.Equal("target-refresh-token", targetMetadata.RefreshToken);
        Assert.Equal("other-access-token", otherMetadata.AccessToken);
        Assert.Equal("other-refresh-token", otherMetadata.RefreshToken);
    }

    [Fact]
    public async Task StartGmailOAuthAsync_AllHistorySelection_SetsBackfillAllHistory()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient();

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = CreateService(dbContext, clock, gmailClient);

        await service.StartGmailOAuthAsync(
            new RegisterInboxAccountRequest
            {
                EmailAddress = "alex.personal@example.com",
                BackfillMonths = 0
            },
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            "/",
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName,
            CancellationToken.None);

        var account = Assert.Single(dbContext.InboxAccounts);
        var metadata = InboxAccountMetadataJson.Read(account, _ => new InboxAccountMetadata());

        Assert.Null(metadata.BackfillMonths);
        Assert.True(metadata.BackfillAllHistory);
    }

    [Fact]
    public async Task UpdateGmailBackfillAsync_ConnectedAccount_UpdatesWindowAndQueuesBackfill()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var account = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "alex.personal@example.com"
        };
        InboxAccountMetadataJson.Write(
            account,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                NextStep = "Gmail is connected. Careeroc is running lightweight recruiting-email sync.",
                BackfillMonths = 6,
                BackfillAllHistory = false,
                InitialBackfillCompletedAtUtc = clock.UtcNow.AddDays(-1)
            });

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(account);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock);
        var response = await service.UpdateGmailBackfillAsync(
            account.Id,
            new UpdateInboxBackfillRequest { BackfillMonths = 0 },
            ApplicationDefaults.DefaultUserEmail,
            CancellationToken.None);

        var dto = Assert.Single(response!.Accounts);
        var metadata = InboxAccountMetadataJson.Read(account, _ => new InboxAccountMetadata());

        Assert.Null(dto.BackfillMonths);
        Assert.True(dto.BackfillAllHistory);
        Assert.Null(dto.InitialBackfillCompletedAtUtc);
        Assert.Null(metadata.BackfillMonths);
        Assert.True(metadata.BackfillAllHistory);
        Assert.Null(metadata.InitialBackfillCompletedAtUtc);
        Assert.Contains("all available history", metadata.NextStep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartGmailOAuthAsync_RejectsExternalReturnUrls()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient();

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = CreateService(dbContext, clock, gmailClient);

        await service.StartGmailOAuthAsync(
            new RegisterInboxAccountRequest
            {
                EmailAddress = "alex.personal@example.com"
            },
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            "https://evil.example/phish",
            ApplicationDefaults.DefaultUserEmail,
            ApplicationDefaults.DefaultUserName,
            CancellationToken.None);

        var account = Assert.Single(dbContext.InboxAccounts);
        var metadata = InboxAccountMetadataJson.Read(account, _ => new InboxAccountMetadata());

        Assert.Equal("/", metadata.OAuthReturnUrl);
    }

    [Fact]
    public async Task DeleteInboxAccountAsync_SoftDisconnectsRequestedGmailAccount()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        var connected = new InboxAccount
        {
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "connected@gmail.com"
        };
        var failed = new InboxAccount
        {
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "failed@gmail.com"
        };
        InboxAccountMetadataJson.Write(
            failed,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                NextStep = "Connected.",
                AccessToken = "protected-access-token",
                RefreshToken = "protected-refresh-token",
                AccessTokenExpiresAtUtc = clock.UtcNow.AddHours(1),
                GmailHistoryId = "history-123"
            });

        user.InboxAccounts.Add(connected);
        user.InboxAccounts.Add(failed);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock);
        var response = await service.DeleteInboxAccountAsync(
            failed.Id,
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(response);
        Assert.Equal(2, dbContext.InboxAccounts.Count());
        var disconnected = dbContext.InboxAccounts.Single(account => account.EmailAddress == "failed@gmail.com");
        var metadata = InboxAccountMetadataJson.Read(disconnected, _ => new InboxAccountMetadata());
        Assert.Equal("Reconnect required", metadata.ConnectionStatus);
        Assert.Null(metadata.AccessToken);
        Assert.Null(metadata.RefreshToken);
        Assert.Null(metadata.AccessTokenExpiresAtUtc);
        Assert.Null(metadata.GmailHistoryId);
        Assert.Contains(response!.Accounts, account => account.EmailAddress == "connected@gmail.com");
        Assert.Contains(response.Accounts, account => account.EmailAddress == "failed@gmail.com");
    }

    [Fact]
    public async Task DeleteInboxAccountAsync_RemovesUnconnectedGmailAccountWithoutImportedMail()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var pending = new InboxAccount
        {
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "pending@gmail.com"
        };
        InboxAccountMetadataJson.Write(
            pending,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Waiting for Google",
                NextStep = "Finish Google consent.",
                OAuthState = "state",
                OAuthCodeVerifier = "verifier",
                OAuthReturnUrl = "/app/inbox",
                OAuthStateExpiresAtUtc = clock.UtcNow.AddMinutes(15)
            });

        user.InboxAccounts.Add(pending);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock);
        var response = await service.DeleteInboxAccountAsync(
            pending.Id,
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(response);
        Assert.Empty(dbContext.InboxAccounts);
        Assert.Empty(response!.Accounts);
    }

    [Fact]
    public async Task DeleteInboxAccountAsync_PreservesApplicationsAndEvents()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        var demoInbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.DemoInboxProvider,
            EmailAddress = ApplicationDefaults.DefaultUserEmail
        };
        var gmailInbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "connected@gmail.com"
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Vercel",
            Role = "Staff Product Engineer",
            Status = Domain.Enums.ApplicationStatus.Offer
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.AddRange(demoInbox, gmailInbox);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.AddRange(
            new InboxEmail
            {
                InboxAccountId = demoInbox.Id,
                InboxAccount = demoInbox,
                JobApplicationId = application.Id,
                JobApplication = application,
                ExternalMessageId = "mock-application-1",
                ThreadId = "mock-thread-1",
                Sender = "careers@vercel.com",
                Subject = "Sample Vercel update",
                RawBody = "Sample",
                ParsedBody = "Sample",
                ReceivedAtUtc = clock.UtcNow
            },
            new InboxEmail
            {
                InboxAccountId = gmailInbox.Id,
                InboxAccount = gmailInbox,
                ExternalMessageId = "gmail-newsletter-1",
                ThreadId = "gmail-thread-1",
                Sender = "hello@example.com",
                Subject = "General inbox message",
                RawBody = "Hello",
                ParsedBody = "Hello",
                ReceivedAtUtc = clock.UtcNow
            });
        dbContext.Reminders.Add(
            new Reminder
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                Type = Domain.Enums.ReminderType.FollowUp,
                Status = Domain.Enums.ReminderStatus.Open,
                Title = "Reply to recruiter at Vercel",
                DueAtUtc = clock.UtcNow.AddDays(1)
            });
        dbContext.JobApplicationEvents.Add(
            new JobApplicationEvent
            {
                JobApplicationId = application.Id,
                JobApplication = application,
                EventType = "offer",
                EventTimeUtc = clock.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock);
        var response = await service.DeleteInboxAccountAsync(
            demoInbox.Id,
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(response);
        Assert.Equal(2, dbContext.InboxAccounts.Count());
        Assert.Single(dbContext.JobApplications);
        Assert.Single(dbContext.Reminders);
        Assert.Single(dbContext.JobApplicationEvents);
        Assert.Contains(response!.Accounts, account => account.EmailAddress == "connected@gmail.com");
    }

    [Fact]
    public async Task DeleteImportedDataAsync_RequiresTypedEmailConfirmation()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var inbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "connected@gmail.com"
        };
        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inbox);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteImportedDataAsync(
                inbox.Id,
                new DeleteImportedInboxDataRequest("wrong@gmail.com"),
                ApplicationDefaults.DefaultUserEmail));

        Assert.Equal("Type the inbox email address to delete imported data.", exception.Message);
    }

    [Fact]
    public async Task DeleteImportedDataAsync_RemovesImportedEmailsAndPreservesApplications()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var inbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "connected@gmail.com"
        };
        InboxAccountMetadataJson.Write(
            inbox,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                NextStep = "Connected.",
                AccessToken = "protected-access-token",
                RefreshToken = "protected-refresh-token",
                AccessTokenExpiresAtUtc = clock.UtcNow.AddHours(1),
                GmailHistoryId = "history-123",
                InitialBackfillCompletedAtUtc = clock.UtcNow,
                SentReplyBackfillCompletedAtUtc = clock.UtcNow
            });
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Vercel",
            Role = "Staff Product Engineer",
            Status = Domain.Enums.ApplicationStatus.Interviewing
        };
        var email = new InboxEmail
        {
            InboxAccountId = inbox.Id,
            InboxAccount = inbox,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "gmail-1",
            ThreadId = "thread-1",
            Sender = "recruiter@vercel.com",
            Subject = "Interview",
            RawBody = "Raw imported content",
            ParsedBody = "Parsed imported content",
            ReceivedAtUtc = clock.UtcNow
        };
        var job = new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = Domain.Enums.EmailProcessingStatus.Pending,
            AvailableAtUtc = clock.UtcNow,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        var linkedEvent = new JobApplicationEvent
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            InboxEmailId = email.Id,
            InboxEmail = email,
            EventType = "interview",
            EventTimeUtc = clock.UtcNow
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inbox);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(email);
        dbContext.EmailProcessingJobs.Add(job);
        dbContext.JobApplicationEvents.Add(linkedEvent);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock);
        var response = await service.DeleteImportedDataAsync(
            inbox.Id,
            new DeleteImportedInboxDataRequest("connected@gmail.com"),
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(response);
        Assert.Equal(1, response!.DeletedEmails);
        Assert.Equal(1, response.DeletedProcessingJobs);
        Assert.Equal(1, response.PreservedApplications);
        Assert.Empty(dbContext.InboxEmails);
        Assert.Empty(dbContext.EmailProcessingJobs);
        Assert.Single(dbContext.JobApplications);
        Assert.Null(dbContext.JobApplicationEvents.Single().InboxEmailId);
        var metadata = InboxAccountMetadataJson.Read(inbox, _ => new InboxAccountMetadata());
        Assert.Equal("Imported data deleted", metadata.ConnectionStatus);
        Assert.Null(metadata.AccessToken);
        Assert.Null(metadata.RefreshToken);
        Assert.Null(metadata.InitialBackfillCompletedAtUtc);
    }

    [Fact]
    public async Task PurgeInboxAndImportedApplicationsAsync_RequiresDeleteConfirmation()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var inbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "connected@gmail.com"
        };
        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inbox);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PurgeInboxAndImportedApplicationsAsync(
                inbox.Id,
                new DeleteInboxPurgeRequest("connected@gmail.com"),
                ApplicationDefaults.DefaultUserEmail));

        Assert.Equal(
            "Type DELETE connected@gmail.com to purge this inbox and its imported applications.",
            exception.Message);
        Assert.Single(dbContext.InboxAccounts);
    }

    [Fact]
    public async Task PurgeInboxAndImportedApplicationsAsync_RemovesInboxAndExclusiveImportedApplications()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var clock = new TestClock(new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero));

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var otherUser = new User
        {
            Email = "other@example.com",
            Name = "Other User"
        };
        var inbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "connected@gmail.com"
        };
        var secondInbox = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "archive@gmail.com"
        };
        var otherInbox = new InboxAccount
        {
            UserId = otherUser.Id,
            User = otherUser,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "other@gmail.com"
        };
        var exclusiveApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Vercel",
            Role = "Staff Product Engineer",
            Source = "Email",
            Status = Domain.Enums.ApplicationStatus.Interviewing
        };
        var sharedApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Figma",
            Role = "Product Engineer",
            Source = "Email",
            Status = Domain.Enums.ApplicationStatus.Applied
        };
        var manualApplication = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "Manual Co",
            Role = "Backend Engineer",
            Source = "Manual",
            Status = Domain.Enums.ApplicationStatus.Applied
        };
        var otherApplication = new JobApplication
        {
            UserId = otherUser.Id,
            User = otherUser,
            Company = "Other Co",
            Role = "Hidden Role",
            Source = "Email",
            Status = Domain.Enums.ApplicationStatus.Offer
        };
        var exclusiveEmail = new InboxEmail
        {
            InboxAccountId = inbox.Id,
            InboxAccount = inbox,
            JobApplicationId = exclusiveApplication.Id,
            JobApplication = exclusiveApplication,
            ExternalMessageId = "gmail-exclusive",
            ThreadId = "thread-exclusive",
            Sender = "recruiter@vercel.com",
            Subject = "Interview",
            RawBody = "Raw exclusive",
            ParsedBody = "Parsed exclusive",
            ReceivedAtUtc = clock.UtcNow
        };
        var sharedSelectedEmail = new InboxEmail
        {
            InboxAccountId = inbox.Id,
            InboxAccount = inbox,
            JobApplicationId = sharedApplication.Id,
            JobApplication = sharedApplication,
            ExternalMessageId = "gmail-shared-selected",
            ThreadId = "thread-shared-selected",
            Sender = "recruiter@figma.com",
            Subject = "Interview",
            RawBody = "Raw shared selected",
            ParsedBody = "Parsed shared selected",
            ReceivedAtUtc = clock.UtcNow
        };
        var sharedOtherEmail = new InboxEmail
        {
            InboxAccountId = secondInbox.Id,
            InboxAccount = secondInbox,
            JobApplicationId = sharedApplication.Id,
            JobApplication = sharedApplication,
            ExternalMessageId = "gmail-shared-other",
            ThreadId = "thread-shared-other",
            Sender = "recruiter@figma.com",
            Subject = "Follow up",
            RawBody = "Raw shared other",
            ParsedBody = "Parsed shared other",
            ReceivedAtUtc = clock.UtcNow
        };
        var manualEmail = new InboxEmail
        {
            InboxAccountId = inbox.Id,
            InboxAccount = inbox,
            JobApplicationId = manualApplication.Id,
            JobApplication = manualApplication,
            ExternalMessageId = "gmail-manual",
            ThreadId = "thread-manual",
            Sender = "recruiter@manual.example",
            Subject = "Manual evidence",
            RawBody = "Raw manual",
            ParsedBody = "Parsed manual",
            ReceivedAtUtc = clock.UtcNow
        };
        var otherEmail = new InboxEmail
        {
            InboxAccountId = otherInbox.Id,
            InboxAccount = otherInbox,
            JobApplicationId = otherApplication.Id,
            JobApplication = otherApplication,
            ExternalMessageId = "gmail-other",
            ThreadId = "thread-other",
            Sender = "recruiter@other.example",
            Subject = "Offer",
            RawBody = "Raw other",
            ParsedBody = "Parsed other",
            ReceivedAtUtc = clock.UtcNow
        };

        dbContext.Users.AddRange(user, otherUser);
        dbContext.InboxAccounts.AddRange(inbox, secondInbox, otherInbox);
        dbContext.JobApplications.AddRange(exclusiveApplication, sharedApplication, manualApplication, otherApplication);
        dbContext.InboxEmails.AddRange(exclusiveEmail, sharedSelectedEmail, sharedOtherEmail, manualEmail, otherEmail);
        dbContext.EmailProcessingJobs.AddRange(
            CreateProcessingJob(exclusiveEmail, clock),
            CreateProcessingJob(sharedSelectedEmail, clock),
            CreateProcessingJob(sharedOtherEmail, clock),
            CreateProcessingJob(manualEmail, clock));
        dbContext.Reminders.AddRange(
            CreateReminder(exclusiveApplication, clock),
            CreateReminder(sharedApplication, clock),
            CreateReminder(manualApplication, clock));
        dbContext.JobApplicationEvents.AddRange(
            CreateEvent(exclusiveApplication, exclusiveEmail, "interview", clock),
            CreateEvent(sharedApplication, sharedSelectedEmail, "interview", clock),
            CreateEvent(sharedApplication, sharedOtherEmail, "follow_up", clock),
            CreateEvent(manualApplication, manualEmail, "manual_evidence", clock));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, clock);
        var response = await service.PurgeInboxAndImportedApplicationsAsync(
            inbox.Id,
            new DeleteInboxPurgeRequest("DELETE connected@gmail.com"),
            ApplicationDefaults.DefaultUserEmail);

        Assert.NotNull(response);
        Assert.Equal(3, response!.DeletedEmails);
        Assert.Equal(3, response.DeletedProcessingJobs);
        Assert.Equal(1, response.DeletedApplications);
        Assert.Equal(1, response.DeletedReminders);
        Assert.Equal(1, response.DeletedEvents);
        Assert.Equal(2, response.UnlinkedEvents);
        Assert.Equal(1, response.DeletedInboxAccounts);
        Assert.DoesNotContain(dbContext.InboxAccounts, account => account.Id == inbox.Id);
        Assert.DoesNotContain(dbContext.JobApplications, application => application.Id == exclusiveApplication.Id);
        Assert.Contains(dbContext.JobApplications, application => application.Id == sharedApplication.Id);
        Assert.Contains(dbContext.JobApplications, application => application.Id == manualApplication.Id);
        Assert.Contains(dbContext.JobApplications, application => application.Id == otherApplication.Id);
        Assert.DoesNotContain(dbContext.InboxEmails, email => email.InboxAccountId == inbox.Id);
        Assert.Contains(dbContext.InboxEmails, email => email.Id == sharedOtherEmail.Id);
        Assert.Contains(dbContext.InboxEmails, email => email.Id == otherEmail.Id);
        Assert.Equal(1, dbContext.EmailProcessingJobs.Count());
        Assert.Equal(sharedOtherEmail.Id, dbContext.EmailProcessingJobs.Single().InboxEmailId);
        Assert.Equal(2, dbContext.Reminders.Count());
        Assert.DoesNotContain(dbContext.JobApplicationEvents, @event => @event.JobApplicationId == exclusiveApplication.Id);
        Assert.Null(dbContext.JobApplicationEvents.Single(@event => @event.EventType == "interview").InboxEmailId);
        Assert.Equal(
            sharedOtherEmail.Id,
            dbContext.JobApplicationEvents.Single(@event => @event.EventType == "follow_up").InboxEmailId);
        Assert.Null(dbContext.JobApplicationEvents.Single(@event => @event.EventType == "manual_evidence").InboxEmailId);
    }

    private static InboxSetupService CreateService(
        Infrastructure.Persistence.CareerocDbContext dbContext,
        TestClock clock,
        FakeGmailInboxClient? gmailClient = null)
    {
        return new InboxSetupService(
            dbContext,
            gmailClient ?? new FakeGmailInboxClient(),
            clock);
    }

    private static EmailProcessingJob CreateProcessingJob(InboxEmail email, TestClock clock)
    {
        return new EmailProcessingJob
        {
            InboxEmailId = email.Id,
            InboxEmail = email,
            Status = Domain.Enums.EmailProcessingStatus.Pending,
            AvailableAtUtc = clock.UtcNow,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
    }

    private static Reminder CreateReminder(JobApplication application, TestClock clock)
    {
        return new Reminder
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            Type = Domain.Enums.ReminderType.FollowUp,
            Status = Domain.Enums.ReminderStatus.Open,
            Title = $"Follow up with {application.Company}",
            DueAtUtc = clock.UtcNow.AddDays(1)
        };
    }

    private static JobApplicationEvent CreateEvent(
        JobApplication application,
        InboxEmail email,
        string eventType,
        TestClock clock)
    {
        return new JobApplicationEvent
        {
            JobApplicationId = application.Id,
            JobApplication = application,
            InboxEmailId = email.Id,
            InboxEmail = email,
            EventType = eventType,
            EventTimeUtc = clock.UtcNow
        };
    }

    private sealed class FakeGmailInboxClient : IGmailInboxClient
    {
        public bool IsConfigured { get; set; } = true;
        public string? LastLoginHint { get; private set; }
        public GmailAuthorizationResult ExchangeResult { get; set; } =
            new("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1));
        public GmailAuthorizationResult RefreshResult { get; set; } =
            new("refreshed-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1));
        public GmailProfile Profile { get; set; } = new("alex.personal@example.com", "history-123");

        public string BuildAuthorizationUrl(string state, string codeChallenge, string redirectUri, string? loginHint)
        {
            LastLoginHint = loginHint;
            return $"https://accounts.google.com/o/oauth2/v2/auth?state={state}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
        }

        public Task<GmailAuthorizationResult> ExchangeCodeAsync(
            string code,
            string codeVerifier,
            string redirectUri,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExchangeResult);
        }

        public Task<GmailAuthorizationResult> RefreshAccessTokenAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RefreshResult);
        }

        public Task<GmailProfile> GetProfileAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Profile);
        }

        public Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
            string accessToken,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ExternalInboxEmail>>([]);
        }

        public Task<IReadOnlyList<ExternalInboxEmail>> SearchRecruitingEmailsAsync(
            string accessToken,
            IReadOnlySet<string>? existingMessageIds = null,
            IReadOnlySet<string>? existingThreadIds = null,
            DateTimeOffset? receivedAfterUtc = null,
            DateTimeOffset? sentAfterUtc = null,
            string? mailboxEmailAddress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ExternalInboxEmail>>([]);
        }

        public Task<GmailHistorySyncResult> GetIncrementalRecruitingEmailsAsync(
            string accessToken,
            string startHistoryId,
            IReadOnlySet<string>? existingMessageIds = null,
            IReadOnlySet<string>? existingThreadIds = null,
            string? mailboxEmailAddress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GmailHistorySyncResult([], startHistoryId));
        }
    }
}
