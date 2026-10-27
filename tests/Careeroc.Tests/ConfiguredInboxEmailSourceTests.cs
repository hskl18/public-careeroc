using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Infrastructure.Demo;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Configuration;

namespace Careeroc.Tests;

public sealed class ConfiguredInboxEmailSourceTests
{
    [Fact]
    public async Task GetRecentEmailsAsync_UsesIncrementalHistory_WhenMetadataIsReady()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient
        {
            IncrementalResult = new GmailHistorySyncResult(
                [
                    new ExternalInboxEmail(
                        "message-1",
                        "thread-1",
                        "Recruiter <recruiting@stripe.com>",
                        "Interview update",
                        "Please pick a slot.",
                        clock.UtcNow)
                ],
                "history-new")
        };
        var source = new ConfiguredInboxEmailSource(
            new DemoInboxEmailSource(clock),
            gmailClient,
            NoopInboxAccountTokenProtector.Instance,
            clock);
        var inboxAccount = CreateConnectedGmailAccount(clock);
        var metadata = InboxAccountMetadataJson.Read(inboxAccount, _ => new InboxAccountMetadata());
        metadata.GmailHistoryId = "history-old";
        metadata.InitialBackfillCompletedAtUtc = clock.UtcNow.AddDays(-7);
        metadata.SentReplyBackfillCompletedAtUtc = clock.UtcNow.AddDays(-7);
        InboxAccountMetadataJson.Write(inboxAccount, metadata);

        var emails = await source.GetRecentEmailsAsync(
            inboxAccount,
            existingMessageIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known-message" },
            existingThreadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known-thread" },
            cancellationToken: CancellationToken.None);

        var updatedMetadata = InboxAccountMetadataJson.Read(inboxAccount, _ => new InboxAccountMetadata());

        Assert.True(gmailClient.IncrementalCalled);
        Assert.False(gmailClient.SearchCalled);
        Assert.Equal("history-old", gmailClient.LastStartHistoryId);
        Assert.Equal("history-new", updatedMetadata.GmailHistoryId);
        Assert.Single(emails);
    }

    [Fact]
    public async Task GetRecentEmailsAsync_FallsBackToFullSearch_WhenHistoryCursorExpires()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient
        {
            IncrementalResult = new GmailHistorySyncResult([], null, RequiresFullSyncFallback: true),
            SearchResult =
            [
                new ExternalInboxEmail(
                    "message-2",
                    "thread-2",
                    "Recruiter <recruiting@vercel.com>",
                    "Application update",
                    "We would like to move forward.",
                    clock.UtcNow)
            ],
            Profile = new GmailProfile("alex.personal@example.com", "history-reset")
        };
        var source = new ConfiguredInboxEmailSource(
            new DemoInboxEmailSource(clock),
            gmailClient,
            NoopInboxAccountTokenProtector.Instance,
            clock);
        var inboxAccount = CreateConnectedGmailAccount(clock);
        var metadata = InboxAccountMetadataJson.Read(inboxAccount, _ => new InboxAccountMetadata());
        metadata.GmailHistoryId = "history-stale";
        metadata.InitialBackfillCompletedAtUtc = clock.UtcNow.AddDays(-7);
        metadata.SentReplyBackfillCompletedAtUtc = clock.UtcNow.AddDays(-7);
        InboxAccountMetadataJson.Write(inboxAccount, metadata);

        var emails = await source.GetRecentEmailsAsync(
            inboxAccount,
            existingMessageIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            existingThreadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken: CancellationToken.None);

        var updatedMetadata = InboxAccountMetadataJson.Read(inboxAccount, _ => new InboxAccountMetadata());

        Assert.True(gmailClient.IncrementalCalled);
        Assert.True(gmailClient.SearchCalled);
        Assert.Equal("history-reset", updatedMetadata.GmailHistoryId);
        Assert.Single(emails);
    }

    [Fact]
    public async Task GetRecentEmailsAsync_UnprotectsStoredGmailTokens()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero));
        var gmailClient = new FakeGmailInboxClient
        {
            IncrementalResult = new GmailHistorySyncResult([], "history-new")
        };
        var tokenProtector = CreateTokenProtector();
        var source = new ConfiguredInboxEmailSource(
            new DemoInboxEmailSource(clock),
            gmailClient,
            tokenProtector,
            clock);
        var inboxAccount = CreateConnectedGmailAccount(clock);
        var metadata = InboxAccountMetadataJson.Read(inboxAccount, _ => new InboxAccountMetadata());
        metadata.AccessToken = tokenProtector.Protect("access-token");
        metadata.RefreshToken = tokenProtector.Protect("refresh-token");
        metadata.GmailHistoryId = "history-old";
        metadata.InitialBackfillCompletedAtUtc = clock.UtcNow.AddDays(-7);
        InboxAccountMetadataJson.Write(inboxAccount, metadata);

        await source.GetRecentEmailsAsync(
            inboxAccount,
            existingMessageIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            existingThreadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken: CancellationToken.None);

        Assert.Equal("access-token", gmailClient.LastAccessToken);
    }

    [Fact]
    public async Task GetRecentEmailsAsync_MarksReconnectRequired_WhenProtectedTokensCannotDecrypt()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 23, 18, 0, 0, TimeSpan.Zero));
        var source = new ConfiguredInboxEmailSource(
            new DemoInboxEmailSource(clock),
            new FakeGmailInboxClient(),
            new ThrowingInboxAccountTokenProtector(),
            clock);
        var inboxAccount = CreateConnectedGmailAccount(clock);

        var emails = await source.GetRecentEmailsAsync(
            inboxAccount,
            existingMessageIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            existingThreadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken: CancellationToken.None);

        var metadata = InboxAccountMetadataJson.Read(inboxAccount, _ => new InboxAccountMetadata());

        Assert.Empty(emails);
        Assert.Equal("Reconnect required", metadata.ConnectionStatus);
        Assert.Contains("could not be decrypted", metadata.NextStep, StringComparison.OrdinalIgnoreCase);
    }

    private static InboxAccount CreateConnectedGmailAccount(TestClock clock)
    {
        var account = new InboxAccount
        {
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = "alex.personal@example.com",
            LastSyncedAtUtc = clock.UtcNow.AddMinutes(-5)
        };

        InboxAccountMetadataJson.Write(
            account,
            new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Connected",
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
            });

        return account;
    }

    private static ConfiguredInboxAccountTokenProtector CreateTokenProtector()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Security:TokenProtectionKey"] = "test-token-protection-key-that-is-long-enough"
                })
            .Build();

        return new ConfiguredInboxAccountTokenProtector(configuration);
    }

    private sealed class FakeGmailInboxClient : IGmailInboxClient
    {
        public bool IsConfigured => true;
        public bool SearchCalled { get; private set; }
        public bool IncrementalCalled { get; private set; }
        public string? LastStartHistoryId { get; private set; }
        public string? LastAccessToken { get; private set; }
        public GmailProfile Profile { get; set; } = new("alex.personal@example.com", "history-profile");
        public IReadOnlyList<ExternalInboxEmail> SearchResult { get; set; } = [];
        public GmailHistorySyncResult IncrementalResult { get; set; } = new([], "history-profile");

        public string BuildAuthorizationUrl(string state, string codeChallenge, string redirectUri, string? loginHint)
        {
            throw new NotSupportedException();
        }

        public Task<GmailAuthorizationResult> ExchangeCodeAsync(
            string code,
            string codeVerifier,
            string redirectUri,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<GmailAuthorizationResult> RefreshAccessTokenAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
            throw new NotSupportedException();
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
            LastAccessToken = accessToken;
            SearchCalled = true;
            return Task.FromResult(SearchResult);
        }

        public Task<GmailHistorySyncResult> GetIncrementalRecruitingEmailsAsync(
            string accessToken,
            string startHistoryId,
            IReadOnlySet<string>? existingMessageIds = null,
            IReadOnlySet<string>? existingThreadIds = null,
            string? mailboxEmailAddress = null,
            CancellationToken cancellationToken = default)
        {
            LastAccessToken = accessToken;
            IncrementalCalled = true;
            LastStartHistoryId = startHistoryId;
            return Task.FromResult(IncrementalResult);
        }
    }

    private sealed class ThrowingInboxAccountTokenProtector : IInboxAccountTokenProtector
    {
        public string? Protect(string? token)
        {
            return token;
        }

        public string? Unprotect(string? token)
        {
            throw new InvalidOperationException("Token key mismatch.");
        }
    }
}
