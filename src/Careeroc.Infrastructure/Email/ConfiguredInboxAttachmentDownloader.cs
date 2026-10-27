using System.Security.Cryptography;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;

namespace Careeroc.Infrastructure.Email;

public sealed class ConfiguredInboxAttachmentDownloader : IInboxAttachmentDownloader
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IGmailInboxClient _gmailInboxClient;
    private readonly IInboxAccountTokenProtector _tokenProtector;
    private readonly IClock _clock;

    public ConfiguredInboxAttachmentDownloader(
        ICareerocDbContext dbContext,
        IGmailInboxClient gmailInboxClient,
        IInboxAccountTokenProtector tokenProtector,
        IClock clock)
    {
        _dbContext = dbContext;
        _gmailInboxClient = gmailInboxClient;
        _tokenProtector = tokenProtector;
        _clock = clock;
    }

    public async Task<IReadOnlyList<DownloadedInboxAttachment>> DownloadSupportedAttachmentsAsync(
        InboxEmail inboxEmail,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(inboxEmail.InboxAccount.Provider, ApplicationDefaults.GmailInboxProvider, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var accessToken = await ResolveAccessTokenAsync(inboxEmail.InboxAccount, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return [];
        }

        return await _gmailInboxClient.DownloadSupportedAttachmentsAsync(
            accessToken,
            inboxEmail.ExternalMessageId,
            maxBytes,
            cancellationToken);
    }

    private async Task<string?> ResolveAccessTokenAsync(
        InboxAccount inboxAccount,
        CancellationToken cancellationToken)
    {
        var metadata = InboxAccountMetadataJson.Read(inboxAccount, _ => new InboxAccountMetadata());
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
            metadata.NextStep = "Stored Gmail tokens could not be decrypted. Reconnect Gmail before attachment import can run.";
            InboxAccountMetadataJson.Write(inboxAccount, metadata);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (string.IsNullOrWhiteSpace(accessToken) ||
            !metadata.AccessTokenExpiresAtUtc.HasValue ||
            metadata.AccessTokenExpiresAtUtc <= _clock.UtcNow.AddMinutes(1))
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
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
            metadata.NextStep = "Gmail is connected. Attachment evidence import can download supported recruiting attachments.";
            InboxAccountMetadataJson.Write(inboxAccount, metadata);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return accessToken;
    }
}
