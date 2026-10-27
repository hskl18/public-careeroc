using System.Security.Cryptography;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Infrastructure.Demo;

namespace Careeroc.Infrastructure.Email;

public sealed class ConfiguredInboxEmailSource : IInboxEmailSource
{
    private const int DefaultBackfillMonths = 3;

    private readonly DemoInboxEmailSource _demoInboxEmailSource;
    private readonly IGmailInboxClient _gmailInboxClient;
    private readonly IInboxAccountTokenProtector _tokenProtector;
    private readonly IClock _clock;

    public ConfiguredInboxEmailSource(
        DemoInboxEmailSource demoInboxEmailSource,
        IGmailInboxClient gmailInboxClient,
        IInboxAccountTokenProtector tokenProtector,
        IClock clock)
    {
        _demoInboxEmailSource = demoInboxEmailSource;
        _gmailInboxClient = gmailInboxClient;
        _tokenProtector = tokenProtector;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
        InboxAccount inboxAccount,
        IReadOnlySet<string>? existingMessageIds = null,
        IReadOnlySet<string>? existingThreadIds = null,
        CancellationToken cancellationToken = default)
    {
        return inboxAccount.Provider switch
        {
            ApplicationDefaults.GmailInboxProvider => await GetGmailEmailsAsync(
                inboxAccount,
                existingMessageIds,
                existingThreadIds,
                cancellationToken),
            ApplicationDefaults.DemoInboxProvider => await _demoInboxEmailSource.GetRecentEmailsAsync(
                inboxAccount,
                existingMessageIds,
                existingThreadIds,
                cancellationToken),
            _ => []
        };
    }

    private async Task<IReadOnlyList<ExternalInboxEmail>> GetGmailEmailsAsync(
        InboxAccount inboxAccount,
        IReadOnlySet<string>? existingMessageIds,
        IReadOnlySet<string>? existingThreadIds,
        CancellationToken cancellationToken)
    {
        var metadata = InboxAccountMetadataJson.Read(
            inboxAccount,
            _ => new InboxAccountMetadata
            {
                SetupMode = "oauth",
                ConnectionStatus = "Reconnect required",
                NextStep = "Reconnect Gmail to restore Gmail API access.",
                BackfillMonths = DefaultBackfillMonths
            });

        if (!metadata.BackfillAllHistory && !metadata.BackfillMonths.HasValue)
        {
            metadata.BackfillMonths = DefaultBackfillMonths;
        }

        string? accessToken;
        string? refreshToken;
        try
        {
            accessToken = _tokenProtector.Unprotect(metadata.AccessToken);
            refreshToken = _tokenProtector.Unprotect(metadata.RefreshToken);
        }
        catch (Exception exception) when (
            exception is CryptographicException or FormatException or InvalidOperationException)
        {
            metadata.ConnectionStatus = "Reconnect required";
            metadata.NextStep = "Stored Gmail tokens could not be decrypted. Reconnect Gmail using this deployment's token protection key.";
            InboxAccountMetadataJson.Write(inboxAccount, metadata);
            return [];
        }

        if (string.IsNullOrWhiteSpace(refreshToken) &&
            string.IsNullOrWhiteSpace(accessToken))
        {
            metadata.ConnectionStatus = "Reconnect required";
            metadata.NextStep = "Continue with Google again to store Gmail access tokens.";
            InboxAccountMetadataJson.Write(inboxAccount, metadata);
            return [];
        }

        try
        {
            if (string.IsNullOrWhiteSpace(accessToken) ||
                !metadata.AccessTokenExpiresAtUtc.HasValue ||
                metadata.AccessTokenExpiresAtUtc <= _clock.UtcNow.AddMinutes(1))
            {
                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    metadata.ConnectionStatus = "Reconnect required";
                    metadata.NextStep = "No Gmail refresh token is available. Continue with Google again.";
                    InboxAccountMetadataJson.Write(inboxAccount, metadata);
                    return [];
                }

                var refreshedToken = await _gmailInboxClient.RefreshAccessTokenAsync(
                    refreshToken,
                    cancellationToken);

                accessToken = refreshedToken.AccessToken;
                refreshToken = refreshedToken.RefreshToken ?? refreshToken;
                metadata.AccessToken = _tokenProtector.Protect(accessToken);
                metadata.RefreshToken = _tokenProtector.Protect(refreshToken);
                metadata.AccessTokenExpiresAtUtc = refreshedToken.ExpiresAtUtc;
                metadata.ConnectionStatus = "Connected";
                metadata.NextStep = "Gmail is connected. The sync worker can pull recent emails from the Gmail API.";
                InboxAccountMetadataJson.Write(inboxAccount, metadata);
            }

            var syncWindowStartUtc = ResolveSyncWindowStartUtc(inboxAccount, metadata);
            var sentSyncWindowStartUtc = ResolveSentSyncWindowStartUtc(metadata, syncWindowStartUtc);
            IReadOnlyList<ExternalInboxEmail> emails;

            if (CanUseIncrementalHistory(metadata))
            {
                var historySync = await _gmailInboxClient.GetIncrementalRecruitingEmailsAsync(
                    accessToken!,
                    metadata.GmailHistoryId!,
                    existingMessageIds,
                    existingThreadIds,
                    inboxAccount.EmailAddress,
                    cancellationToken);

                if (historySync.RequiresFullSyncFallback)
                {
                    emails = await _gmailInboxClient.SearchRecruitingEmailsAsync(
                        accessToken!,
                        existingMessageIds,
                        existingThreadIds,
                        syncWindowStartUtc,
                        sentSyncWindowStartUtc,
                        inboxAccount.EmailAddress,
                        cancellationToken);

                    var profile = await _gmailInboxClient.GetProfileAsync(accessToken!, cancellationToken);
                    ApplyGmailProfile(metadata, profile);
                }
                else
                {
                    emails = historySync.Emails;
                    metadata.GmailHistoryId = historySync.HistoryId ?? metadata.GmailHistoryId;
                }
            }
            else
            {
                emails = await _gmailInboxClient.SearchRecruitingEmailsAsync(
                    accessToken!,
                    existingMessageIds,
                    existingThreadIds,
                    syncWindowStartUtc,
                    sentSyncWindowStartUtc,
                    inboxAccount.EmailAddress,
                    cancellationToken);

                var profile = await _gmailInboxClient.GetProfileAsync(accessToken!, cancellationToken);
                ApplyGmailProfile(metadata, profile);
            }

            metadata.ConnectionStatus = "Connected";
            metadata.NextStep = metadata.InitialBackfillCompletedAtUtc.HasValue
                ? "Gmail is connected. careeroc is running lightweight recruiting-email sync."
                : "Gmail is connected. careeroc is running the initial recruiting-email backfill.";

            if (!metadata.InitialBackfillCompletedAtUtc.HasValue)
            {
                metadata.InitialBackfillCompletedAtUtc = _clock.UtcNow;
                metadata.NextStep = "Gmail is connected. Initial recruiting-email backfill completed.";
            }

            metadata.SentReplyBackfillCompletedAtUtc ??= _clock.UtcNow;

            InboxAccountMetadataJson.Write(inboxAccount, metadata);

            return emails;
        }
        catch (InvalidOperationException exception) when (IsGoogleAuthorizationFailure(exception))
        {
            metadata.ConnectionStatus = "Reconnect required";
            metadata.NextStep = "Google rejected the stored Gmail authorization. Continue with Google again to refresh this inbox connection.";
            InboxAccountMetadataJson.Write(inboxAccount, metadata);
            return [];
        }
    }

    private static bool CanUseIncrementalHistory(InboxAccountMetadata metadata)
    {
        return metadata.InitialBackfillCompletedAtUtc.HasValue &&
               !string.IsNullOrWhiteSpace(metadata.GmailHistoryId);
    }

    private void ApplyGmailProfile(InboxAccountMetadata metadata, GmailProfile profile)
    {
        metadata.GmailHistoryId = profile.HistoryId;
        metadata.GmailMessagesTotal = profile.MessagesTotal;
        metadata.GmailProfileObservedAtUtc = _clock.UtcNow;
    }

    private static bool IsGoogleAuthorizationFailure(InvalidOperationException exception)
    {
        return exception.Message.Contains("Google request failed with 400", StringComparison.Ordinal) ||
               exception.Message.Contains("Google request failed with 401", StringComparison.Ordinal) ||
               exception.Message.Contains("Google request failed with 403", StringComparison.Ordinal);
    }

    private DateTimeOffset? ResolveSyncWindowStartUtc(
        InboxAccount inboxAccount,
        InboxAccountMetadata metadata)
    {
        if (!metadata.InitialBackfillCompletedAtUtc.HasValue)
        {
            return !metadata.BackfillAllHistory && metadata.BackfillMonths.HasValue
                ? _clock.UtcNow.AddMonths(-metadata.BackfillMonths.Value)
                : null;
        }

        if (inboxAccount.LastSyncedAtUtc.HasValue)
        {
            return inboxAccount.LastSyncedAtUtc.Value.AddDays(-1);
        }

        return _clock.UtcNow.AddDays(-14);
    }

    private DateTimeOffset? ResolveSentSyncWindowStartUtc(
        InboxAccountMetadata metadata,
        DateTimeOffset? syncWindowStartUtc)
    {
        if (!metadata.SentReplyBackfillCompletedAtUtc.HasValue)
        {
            return !metadata.BackfillAllHistory && metadata.BackfillMonths.HasValue
                ? _clock.UtcNow.AddMonths(-metadata.BackfillMonths.Value)
                : null;
        }

        return syncWindowStartUtc;
    }
}
