using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public interface IGmailInboxClient
{
    bool IsConfigured { get; }

    string BuildAuthorizationUrl(
        string state,
        string codeChallenge,
        string redirectUri,
        string? loginHint);

    Task<GmailAuthorizationResult> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default);

    Task<GmailAuthorizationResult> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task<GmailProfile> GetProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
        string accessToken,
        int maxResults,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalInboxEmail>> SearchRecruitingEmailsAsync(
        string accessToken,
        IReadOnlySet<string>? existingMessageIds = null,
        IReadOnlySet<string>? existingThreadIds = null,
        DateTimeOffset? receivedAfterUtc = null,
        DateTimeOffset? sentAfterUtc = null,
        string? mailboxEmailAddress = null,
        CancellationToken cancellationToken = default);

    Task<GmailHistorySyncResult> GetIncrementalRecruitingEmailsAsync(
        string accessToken,
        string startHistoryId,
        IReadOnlySet<string>? existingMessageIds = null,
        IReadOnlySet<string>? existingThreadIds = null,
        string? mailboxEmailAddress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadedInboxAttachment>> DownloadSupportedAttachmentsAsync(
        string accessToken,
        string messageId,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DownloadedInboxAttachment>>([]);
    }
}
