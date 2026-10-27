using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Careeroc.Infrastructure.Google;

public sealed class GmailInboxClient : IGmailInboxClient
{
    private const string GmailReadonlyScope = "https://www.googleapis.com/auth/gmail.readonly";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedAttachmentContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/webp"
    };
    private static readonly string[] RecruitingMailboxQueries =
    [
        "\"thank you for applying\"",
        "\"thanks for applying\"",
        "\"your application\"",
        "\"application received\"",
        "\"online assessment\"",
        "\"take-home\"",
        "recruiter",
        "recruiting",
        "interview",
        "assessment",
        "offer",
        "candidacy",
        "\"hiring team\"",
        "\"talent acquisition\"",
        "\"not be moving forward\"",
        "\"next steps\""
    ];

    private readonly HttpClient _httpClient;
    private readonly GmailOptions _options;

    public GmailInboxClient(HttpClient httpClient, IOptions<GmailOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId) &&
        !string.IsNullOrWhiteSpace(_options.ClientSecret);

    public string BuildAuthorizationUrl(
        string state,
        string codeChallenge,
        string redirectUri,
        string? loginHint)
    {
        EnsureConfigured();

        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = $"openid email {GmailReadonlyScope}",
            ["access_type"] = "offline",
            ["include_granted_scopes"] = "true",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["login_hint"] = loginHint
        };

        var query = string.Join(
            "&",
            parameters
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

        return $"https://accounts.google.com/o/oauth2/v2/auth?{query}";
    }

    public async Task<GmailAuthorizationResult> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var response = await _httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("code_verifier", codeVerifier),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("redirect_uri", redirectUri)
            ]),
            cancellationToken);

        return await ParseAuthorizationResultAsync(response, cancellationToken);
    }

    public async Task<GmailAuthorizationResult> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var response = await _httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token")
            ]),
            cancellationToken);

        return await ParseAuthorizationResultAsync(response, cancellationToken);
    }

    public async Task<GmailProfile> GetProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://gmail.googleapis.com/gmail/v1/users/me/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var profile = await JsonSerializer.DeserializeAsync<GmailProfileResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            JsonOptions,
            cancellationToken);

        if (profile is null || string.IsNullOrWhiteSpace(profile.EmailAddress))
        {
            throw new InvalidOperationException("Google did not return a mailbox profile email address.");
        }

        return new GmailProfile(profile.EmailAddress, profile.HistoryId, profile.MessagesTotal);
    }

    public async Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
        string accessToken,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults={Math.Max(1, maxResults)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var listResponse = await JsonSerializer.DeserializeAsync<GmailMessageListResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            JsonOptions,
            cancellationToken);

        if (listResponse?.Messages is null || listResponse.Messages.Count == 0)
        {
            return [];
        }

        var emails = await Task.WhenAll(
            listResponse.Messages.Select(
                message => GetMessageAsync(message.Id, accessToken, cancellationToken)));

        return emails
            .Where(email => email is not null)
            .Cast<ExternalInboxEmail>()
            .OrderByDescending(email => email.ReceivedAtUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<ExternalInboxEmail>> SearchRecruitingEmailsAsync(
        string accessToken,
        IReadOnlySet<string>? existingMessageIds = null,
        IReadOnlySet<string>? existingThreadIds = null,
        DateTimeOffset? receivedAfterUtc = null,
        DateTimeOffset? sentAfterUtc = null,
        string? mailboxEmailAddress = null,
        CancellationToken cancellationToken = default)
    {
        var knownMessageIds = existingMessageIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownThreadIds = existingThreadIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedMessageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recentSentMessageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var query in RecruitingMailboxQueries)
        {
            await CollectMessageIdsForQueryAsync(
                accessToken,
                BuildRecruitingSearchQuery(query, receivedAfterUtc),
                knownMessageIds,
                matchedMessageIds,
                _options.SearchMessageLimit,
                cancellationToken);

            if (matchedMessageIds.Count >= _options.SearchMessageLimit)
            {
                break;
            }
        }

        if (knownThreadIds.Count > 0)
        {
            await CollectMessageIdsForQueryAsync(
                accessToken,
                BuildRecentSentSearchQuery(sentAfterUtc ?? receivedAfterUtc),
                knownMessageIds,
                recentSentMessageIds,
                _options.SearchMessageLimit,
                cancellationToken);
        }

        if (matchedMessageIds.Count == 0 && recentSentMessageIds.Count == 0)
        {
            return [];
        }

        var emailsByMessageId = new Dictionary<string, ExternalInboxEmail>(StringComparer.OrdinalIgnoreCase);
        var recruitingThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in matchedMessageIds.Chunk(10))
        {
            var batchEmails = await Task.WhenAll(
                batch.Select(messageId => GetMessageAsync(messageId, accessToken, cancellationToken, mailboxEmailAddress)));

            foreach (var email in batchEmails.Where(email => email is not null).Cast<ExternalInboxEmail>())
            {
                if (!RecruitingEmailRelevance.IsLikelyRecruiting(email.Sender, email.Subject, email.Body))
                {
                    continue;
                }

                recruitingThreadIds.Add(email.ThreadId);
                if (!knownMessageIds.Contains(email.ExternalMessageId))
                {
                    emailsByMessageId[email.ExternalMessageId] = email;
                }
            }
        }

        foreach (var threadId in recruitingThreadIds.Take(_options.ThreadFetchLimit))
        {
            var threadEmails = await GetThreadMessagesAsync(
                threadId,
                accessToken,
                cancellationToken,
                mailboxEmailAddress);

            foreach (var email in threadEmails)
            {
                if (!knownMessageIds.Contains(email.ExternalMessageId))
                {
                    emailsByMessageId[email.ExternalMessageId] = email;
                }
            }
        }

        foreach (var batch in recentSentMessageIds.Chunk(10))
        {
            var sentEmails = await Task.WhenAll(
                batch.Select(messageId => GetMessageAsync(messageId, accessToken, cancellationToken, mailboxEmailAddress)));

            foreach (var email in sentEmails.Where(email => email is not null).Cast<ExternalInboxEmail>())
            {
                if (email.IsOutbound &&
                    knownThreadIds.Contains(email.ThreadId) &&
                    !knownMessageIds.Contains(email.ExternalMessageId))
                {
                    emailsByMessageId[email.ExternalMessageId] = email;
                }
            }
        }

        return emailsByMessageId.Values
            .Where(
                email =>
                    email.IsOutbound ||
                    recruitingThreadIds.Contains(email.ThreadId) ||
                    RecruitingEmailRelevance.IsLikelyRecruiting(email.Sender, email.Subject, email.Body))
            .OrderByDescending(email => email.ReceivedAtUtc)
            .ToList();
    }

    public async Task<GmailHistorySyncResult> GetIncrementalRecruitingEmailsAsync(
        string accessToken,
        string startHistoryId,
        IReadOnlySet<string>? existingMessageIds = null,
        IReadOnlySet<string>? existingThreadIds = null,
        string? mailboxEmailAddress = null,
        CancellationToken cancellationToken = default)
    {
        var knownMessageIds = existingMessageIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownThreadIds = existingThreadIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changedMessageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var latestHistoryId = startHistoryId;
        string? pageToken = null;
        var historyPagesRead = 0;

        do
        {
            historyPagesRead++;
            var parameters = new Dictionary<string, string?>
            {
                ["maxResults"] = "500",
                ["startHistoryId"] = startHistoryId,
                ["pageToken"] = pageToken,
                ["historyTypes"] = "messageAdded"
            };

            var effectiveParameters = parameters
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            var url = QueryHelpers.AddQueryString(
                "https://gmail.googleapis.com/gmail/v1/users/me/history",
                effectiveParameters);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new GmailHistorySyncResult([], null, RequiresFullSyncFallback: true);
            }

            await EnsureSuccessAsync(response, cancellationToken);

            var historyResponse = await JsonSerializer.DeserializeAsync<GmailHistoryListResponse>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                JsonOptions,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(historyResponse?.HistoryId))
            {
                latestHistoryId = historyResponse.HistoryId;
            }

            if (historyResponse?.History is not null)
            {
                foreach (var historyRecord in historyResponse.History)
                {
                    foreach (var messageAdded in historyRecord.MessagesAdded ?? [])
                    {
                        var messageId = messageAdded.Message?.Id;
                        if (!string.IsNullOrWhiteSpace(messageId) && !knownMessageIds.Contains(messageId))
                        {
                            if (changedMessageIds.Count < _options.HistoryMessageLimit)
                            {
                                changedMessageIds.Add(messageId);
                            }
                        }
                    }
                }
            }

            pageToken = string.IsNullOrWhiteSpace(historyResponse?.NextPageToken)
                ? null
                : historyResponse.NextPageToken;
        }
        while (pageToken is not null &&
               historyPagesRead < _options.HistoryPageLimit &&
               changedMessageIds.Count < _options.HistoryMessageLimit);

        if (changedMessageIds.Count == 0)
        {
            return new GmailHistorySyncResult([], latestHistoryId);
        }

        var changedEmails = await FetchMessagesInChunksAsync(
            changedMessageIds,
            accessToken,
            cancellationToken,
            mailboxEmailAddress);

        var emailsByMessageId = new Dictionary<string, ExternalInboxEmail>(StringComparer.OrdinalIgnoreCase);
        var newRecruitingThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var email in changedEmails.Where(email => email is not null).Cast<ExternalInboxEmail>())
        {
            var isKnownThread = knownThreadIds.Contains(email.ThreadId);
            var isRecruitingRelevant = RecruitingEmailRelevance.IsLikelyRecruiting(email.Sender, email.Subject, email.Body);

            if (isRecruitingRelevant)
            {
                newRecruitingThreadIds.Add(email.ThreadId);
            }

            if (isKnownThread || isRecruitingRelevant || email.IsOutbound && isKnownThread)
            {
                emailsByMessageId[email.ExternalMessageId] = email;
            }
        }

        foreach (var threadId in newRecruitingThreadIds.Where(threadId => !knownThreadIds.Contains(threadId)).Take(_options.ThreadFetchLimit))
        {
            var threadEmails = await GetThreadMessagesAsync(
                threadId,
                accessToken,
                cancellationToken,
                mailboxEmailAddress);

            foreach (var email in threadEmails)
            {
                if (!knownMessageIds.Contains(email.ExternalMessageId))
                {
                    emailsByMessageId[email.ExternalMessageId] = email;
                }
            }
        }

        return new GmailHistorySyncResult(
            emailsByMessageId.Values
                .OrderByDescending(email => email.ReceivedAtUtc)
                .ToList(),
            latestHistoryId);
    }

    public async Task<IReadOnlyList<DownloadedInboxAttachment>> DownloadSupportedAttachmentsAsync(
        string accessToken,
        string messageId,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId) || maxBytes <= 0)
        {
            return [];
        }

        var message = await GetRawMessageAsync(messageId, accessToken, cancellationToken);
        if (message?.Payload is null)
        {
            return [];
        }

        var candidates = CollectAttachmentCandidates(message.Payload);
        if (candidates.Count == 0)
        {
            return [];
        }

        var attachments = new List<DownloadedInboxAttachment>();
        foreach (var candidate in candidates)
        {
            var contentType = ResolveSupportedAttachmentContentType(candidate.ContentType, candidate.FileName);
            if (contentType is null ||
                candidate.SizeBytes.HasValue && candidate.SizeBytes.Value > maxBytes)
            {
                continue;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(candidate.AttachmentId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            await EnsureSuccessAsync(response, cancellationToken);
            var attachmentResponse = await JsonSerializer.DeserializeAsync<GmailAttachmentResponse>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                JsonOptions,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(attachmentResponse?.Data))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = WebEncoders.Base64UrlDecode(attachmentResponse.Data);
            }
            catch (FormatException)
            {
                continue;
            }

            if (bytes.Length == 0 || bytes.Length > maxBytes)
            {
                continue;
            }

            attachments.Add(
                new DownloadedInboxAttachment(
                    candidate.AttachmentId,
                    candidate.FileName,
                    contentType,
                    bytes.Length,
                    bytes));
        }

        return attachments;
    }

    private static string BuildRecruitingSearchQuery(
        string query,
        DateTimeOffset? receivedAfterUtc)
    {
        if (!receivedAfterUtc.HasValue)
        {
            return query;
        }

        return $"{query} after:{receivedAfterUtc.Value.UtcDateTime:yyyy/MM/dd}";
    }

    private static string BuildRecentSentSearchQuery(DateTimeOffset? sentAfterUtc)
    {
        return sentAfterUtc.HasValue
            ? $"in:sent after:{sentAfterUtc.Value.UtcDateTime:yyyy/MM/dd}"
            : "in:sent";
    }

    private async Task<ExternalInboxEmail?> GetMessageAsync(
        string messageId,
        string accessToken,
        CancellationToken cancellationToken,
        string? mailboxEmailAddress = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{messageId}?format=full");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var message = await DeserializeMessageAsync(response, cancellationToken);

        if (message is null)
        {
            return null;
        }

        var sender = GetHeaderValue(message.Payload?.Headers, "From") ?? "Unknown sender";
        var subject = GetHeaderValue(message.Payload?.Headers, "Subject") ?? "(no subject)";
        var dateHeader = GetHeaderValue(message.Payload?.Headers, "Date");
        var body = TruncateBody(ExtractBody(message.Payload) ?? message.Snippet ?? string.Empty);
        var receivedAtUtc = ParseReceivedAtUtc(message.InternalDate, dateHeader);
        var isOutbound = IsOutboundMessage(message, sender, mailboxEmailAddress);

        return new ExternalInboxEmail(
            message.Id,
            message.ThreadId ?? message.Id,
            sender,
            subject,
            body,
            receivedAtUtc,
            isOutbound);
    }

    private async Task<IReadOnlyList<ExternalInboxEmail>> GetThreadMessagesAsync(
        string threadId,
        string accessToken,
        CancellationToken cancellationToken,
        string? mailboxEmailAddress = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://gmail.googleapis.com/gmail/v1/users/me/threads/{threadId}?format=full");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var thread = await JsonSerializer.DeserializeAsync<GmailThreadResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            JsonOptions,
            cancellationToken);

        if (thread?.Messages is null || thread.Messages.Count == 0)
        {
            return [];
        }

        return thread.Messages
            .Select(message => ToExternalEmail(message, mailboxEmailAddress))
            .Where(email => email is not null)
            .Cast<ExternalInboxEmail>()
            .ToList();
    }

    private async Task<GmailMessageResponse?> GetRawMessageAsync(
        string messageId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}?format=full");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await DeserializeMessageAsync(response, cancellationToken);
    }

    private static async Task<GmailMessageResponse?> DeserializeMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        return await JsonSerializer.DeserializeAsync<GmailMessageResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            JsonOptions,
            cancellationToken);
    }

    private ExternalInboxEmail? ToExternalEmail(
        GmailMessageResponse message,
        string? mailboxEmailAddress)
    {
        var sender = GetHeaderValue(message.Payload?.Headers, "From") ?? "Unknown sender";
        var subject = GetHeaderValue(message.Payload?.Headers, "Subject") ?? "(no subject)";
        var dateHeader = GetHeaderValue(message.Payload?.Headers, "Date");
        var body = TruncateBody(ExtractBody(message.Payload) ?? message.Snippet ?? string.Empty);
        var receivedAtUtc = ParseReceivedAtUtc(message.InternalDate, dateHeader);
        var isOutbound = IsOutboundMessage(message, sender, mailboxEmailAddress);

        return new ExternalInboxEmail(
            message.Id,
            message.ThreadId ?? message.Id,
            sender,
            subject,
            body,
            receivedAtUtc,
            isOutbound);
    }

    private async Task CollectMessageIdsForQueryAsync(
        string accessToken,
        string query,
        IReadOnlySet<string> existingMessageIds,
        HashSet<string> matchedMessageIds,
        int messageLimit,
        CancellationToken cancellationToken)
    {
        string? pageToken = null;
        var pagesRead = 0;

        do
        {
            pagesRead++;
            var parameters = new Dictionary<string, string?>
            {
                ["maxResults"] = "100",
                ["q"] = query,
                ["pageToken"] = pageToken
            };

            var effectiveParameters = parameters
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            var url = QueryHelpers.AddQueryString(
                "https://gmail.googleapis.com/gmail/v1/users/me/messages",
                effectiveParameters);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var listResponse = await JsonSerializer.DeserializeAsync<GmailMessageListResponse>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                JsonOptions,
                cancellationToken);

            if (listResponse?.Messages is not null)
            {
                foreach (var message in listResponse.Messages)
                {
                    if (existingMessageIds.Contains(message.Id))
                    {
                        continue;
                    }

                    matchedMessageIds.Add(message.Id);
                    if (matchedMessageIds.Count >= messageLimit)
                    {
                        break;
                    }
                }
            }

            pageToken = string.IsNullOrWhiteSpace(listResponse?.NextPageToken)
                ? null
                : listResponse.NextPageToken;
        }
        while (pageToken is not null &&
               pagesRead < _options.SearchPageLimit &&
               matchedMessageIds.Count < messageLimit);
    }

    private async Task<IReadOnlyList<ExternalInboxEmail?>> FetchMessagesInChunksAsync(
        IEnumerable<string> messageIds,
        string accessToken,
        CancellationToken cancellationToken,
        string? mailboxEmailAddress = null)
    {
        var emails = new List<ExternalInboxEmail?>();
        foreach (var batch in messageIds.Chunk(10))
        {
            var batchEmails = await Task.WhenAll(
                batch.Select(messageId => GetMessageAsync(messageId, accessToken, cancellationToken, mailboxEmailAddress)));
            emails.AddRange(batchEmails);
        }

        return emails;
    }

    private static string? ExtractBody(GmailMessagePayload? payload)
    {
        if (payload is null)
        {
            return null;
        }

        var inlineBody = NormalizeBody(DecodeBody(payload.Body?.Data), payload.MimeType);
        if (!string.IsNullOrWhiteSpace(inlineBody) &&
            (string.IsNullOrWhiteSpace(payload.MimeType) || payload.MimeType.Contains("text/plain", StringComparison.OrdinalIgnoreCase)))
        {
            return inlineBody;
        }

        if (payload.Parts is null || payload.Parts.Count == 0)
        {
            return inlineBody;
        }

        foreach (var part in payload.Parts)
        {
            var nested = ExtractBody(part);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }

        return inlineBody;
    }

    private static string? NormalizeBody(string? body, string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var normalized = mimeType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true
            ? StripHtml(body)
            : body;

        normalized = WebUtility.HtmlDecode(normalized);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private string TruncateBody(string body)
    {
        if (body.Length <= _options.BodyCharacterLimit)
        {
            return body;
        }

        return body[.._options.BodyCharacterLimit];
    }

    private static string StripHtml(string html)
    {
        var withoutScriptOrStyle = Regex.Replace(
            html,
            @"<(script|style)\b[^>]*>.*?</\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withoutTags = Regex.Replace(withoutScriptOrStyle, @"<[^>]+>", " ");
        return withoutTags
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&#160;", " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DecodeBody(string? bodyData)
    {
        if (string.IsNullOrWhiteSpace(bodyData))
        {
            return null;
        }

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(bodyData);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IReadOnlyList<GmailAttachmentCandidate> CollectAttachmentCandidates(GmailMessagePayload payload)
    {
        var candidates = new List<GmailAttachmentCandidate>();
        Visit(payload, candidates);
        return candidates;
    }

    private static void Visit(
        GmailMessagePayload payload,
        List<GmailAttachmentCandidate> candidates)
    {
        if (!string.IsNullOrWhiteSpace(payload.FileName) &&
            !string.IsNullOrWhiteSpace(payload.Body?.AttachmentId))
        {
            candidates.Add(
                new GmailAttachmentCandidate(
                    payload.Body.AttachmentId,
                    Path.GetFileName(payload.FileName),
                    payload.MimeType,
                    payload.Body.Size));
        }

        foreach (var part in payload.Parts ?? [])
        {
            Visit(part, candidates);
        }
    }

    private static string? ResolveSupportedAttachmentContentType(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            SupportedAttachmentContentTypes.Contains(contentType))
        {
            return contentType.ToLowerInvariant();
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };
    }

    private static string? GetHeaderValue(IReadOnlyList<GmailHeader>? headers, string name)
    {
        return headers?.FirstOrDefault(
            header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static DateTimeOffset ParseReceivedAtUtc(string? internalDate, string? dateHeader)
    {
        if (!string.IsNullOrWhiteSpace(internalDate) &&
            long.TryParse(internalDate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        if (!string.IsNullOrWhiteSpace(dateHeader) &&
            DateTimeOffset.TryParse(dateHeader, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDate))
        {
            return parsedDate.ToUniversalTime();
        }

        return DateTimeOffset.UtcNow;
    }

    private static bool IsOutboundMessage(
        GmailMessageResponse message,
        string sender,
        string? mailboxEmailAddress)
    {
        if (message.LabelIds?.Any(label => string.Equals(label, "SENT", StringComparison.OrdinalIgnoreCase)) == true)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(mailboxEmailAddress))
        {
            return false;
        }

        var normalizedMailbox = NormalizeEmailAddress(mailboxEmailAddress);
        var normalizedSender = ExtractEmailAddress(sender);
        return !string.IsNullOrWhiteSpace(normalizedSender) &&
               string.Equals(normalizedSender, normalizedMailbox, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEmailAddress(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string ExtractEmailAddress(string value)
    {
        var match = Regex.Match(value, @"<(?<email>[^>]+)>");
        if (match.Success)
        {
            return match.Groups["email"].Value.Trim().ToLowerInvariant();
        }

        return value.Trim().ToLowerInvariant();
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        throw new InvalidOperationException($"Google request failed with {(int)response.StatusCode}.");
    }

    private async Task<GmailAuthorizationResult> ParseAuthorizationResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenResponse = TryParseAuthorizationResult(body);

        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException(
                "Google did not return an access token.");
        }

        return new GmailAuthorizationResult(
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
    }

    private static GmailTokenResponse? TryParseAuthorizationResult(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        if (TryParseJsonAuthorizationResult(body, out var jsonResult))
        {
            return jsonResult;
        }

        if (TryParseFormAuthorizationResult(body, out var formResult))
        {
            return formResult;
        }

        return null;
    }

    private static bool TryParseJsonAuthorizationResult(string body, out GmailTokenResponse? result)
    {
        result = null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var accessToken =
                TryGetString(root, "access_token") ??
                TryGetString(root, "accessToken");
            var refreshToken =
                TryGetString(root, "refresh_token") ??
                TryGetString(root, "refreshToken");
            var expiresIn =
                TryGetInt32(root, "expires_in") ??
                TryGetInt32(root, "expiresIn");

            if (string.IsNullOrWhiteSpace(accessToken) || expiresIn is null)
            {
                return false;
            }

            result = new GmailTokenResponse(accessToken, refreshToken, expiresIn.Value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseFormAuthorizationResult(string body, out GmailTokenResponse? result)
    {
        result = null;

        var values = QueryHelpers.ParseQuery($"?{body}");
        var accessToken = values.TryGetValue("access_token", out var accessTokenValue)
            ? accessTokenValue.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        var refreshToken = values.TryGetValue("refresh_token", out var refreshTokenValue)
            ? refreshTokenValue.ToString()
            : null;
        var expiresInValue = values.TryGetValue("expires_in", out var expiresInText)
            ? expiresInText.ToString()
            : null;

        if (!int.TryParse(expiresInValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresIn))
        {
            return false;
        }

        result = new GmailTokenResponse(accessToken, refreshToken, expiresIn);
        return true;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Google OAuth is not configured.");
        }
    }

    private sealed record GmailTokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn);

    private sealed record GmailProfileResponse(string EmailAddress, string? HistoryId, int? MessagesTotal);

    private sealed record GmailHistoryListResponse(
        IReadOnlyList<GmailHistoryRecord>? History,
        string? NextPageToken,
        string? HistoryId);

    private sealed record GmailHistoryRecord(
        string? Id,
        IReadOnlyList<GmailMessageAdded>? MessagesAdded);

    private sealed record GmailMessageAdded(GmailMessageReference? Message);

    private sealed record GmailMessageListResponse(
        IReadOnlyList<GmailMessageReference> Messages,
        string? NextPageToken);

    private sealed record GmailMessageReference(string Id);

    private sealed record GmailMessageResponse(
        string Id,
        string? ThreadId,
        string? InternalDate,
        string? Snippet,
        IReadOnlyList<string>? LabelIds,
        GmailMessagePayload? Payload);

    private sealed record GmailThreadResponse(IReadOnlyList<GmailMessageResponse> Messages);

    private sealed record GmailMessagePayload(
        string? FileName,
        string? MimeType,
        GmailMessageBody? Body,
        IReadOnlyList<GmailHeader>? Headers,
        IReadOnlyList<GmailMessagePayload>? Parts);

    private sealed record GmailMessageBody(string? Data, string? AttachmentId, int? Size);

    private sealed record GmailHeader(string Name, string Value);

    private sealed record GmailAttachmentResponse(string? Data);

    private sealed record GmailAttachmentCandidate(
        string AttachmentId,
        string FileName,
        string? ContentType,
        int? SizeBytes);
}
