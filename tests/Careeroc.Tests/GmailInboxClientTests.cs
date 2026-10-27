using System.Net;
using System.Text;
using System.Text.Json;
using Careeroc.Infrastructure.Google;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Careeroc.Tests;

public sealed class GmailInboxClientTests
{
    [Fact]
    public async Task DownloadSupportedAttachmentsAsync_FetchesSmallSupportedAttachment()
    {
        var requestedPaths = new List<string>();
        var pdfBytes = "%PDF-1.7\n"u8.ToArray();

        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                request =>
                {
                    requestedPaths.Add(request.RequestUri!.AbsolutePath);
                    var path = request.RequestUri!.AbsolutePath;

                    if (path.EndsWith("/messages/gmail-message-1", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                """
                                {
                                  "id": "gmail-message-1",
                                  "threadId": "thread-1",
                                  "internalDate": "1775491200000",
                                  "payload": {
                                    "mimeType": "multipart/mixed",
                                    "headers": [],
                                    "parts": [
                                      {
                                        "filename": "offer.pdf",
                                        "mimeType": "application/pdf",
                                        "body": {
                                          "attachmentId": "attachment-1",
                                          "size": 9
                                        }
                                      },
                                      {
                                        "filename": "large.pdf",
                                        "mimeType": "application/pdf",
                                        "body": {
                                          "attachmentId": "too-large",
                                          "size": 9999999
                                        }
                                      },
                                      {
                                        "filename": "notes.exe",
                                        "mimeType": "application/octet-stream",
                                        "body": {
                                          "attachmentId": "unsupported",
                                          "size": 3
                                        }
                                      }
                                    ]
                                  }
                                }
                                """,
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    if (path.EndsWith("/attachments/attachment-1", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                $$"""
                                {
                                  "data": "{{EncodeBase64Url(pdfBytes)}}"
                                }
                                """,
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }));

        var client = new GmailInboxClient(
            httpClient,
            Options.Create(
                new GmailOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }));

        var attachments = await client.DownloadSupportedAttachmentsAsync(
            "access-token",
            "gmail-message-1",
            maxBytes: 1024,
            CancellationToken.None);

        var attachment = Assert.Single(attachments);
        Assert.Equal("attachment-1", attachment.SourceId);
        Assert.Equal("offer.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(pdfBytes, attachment.ContentBytes);
        Assert.DoesNotContain(requestedPaths, path => path.EndsWith("/attachments/too-large", StringComparison.Ordinal));
        Assert.DoesNotContain(requestedPaths, path => path.EndsWith("/attachments/unsupported", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExchangeCodeAsync_ParsesSnakeCaseTokenResponse()
    {
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "access_token": "google-access-token",
                          "refresh_token": "google-refresh-token",
                          "expires_in": 3600
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                }));

        var client = new GmailInboxClient(
            httpClient,
            Options.Create(
                new GmailOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }));

        var result = await client.ExchangeCodeAsync(
            "auth-code",
            "code-verifier",
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            CancellationToken.None);

        Assert.Equal("google-access-token", result.AccessToken);
        Assert.Equal("google-refresh-token", result.RefreshToken);
        Assert.True(result.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(59));
    }

    [Fact]
    public async Task ExchangeCodeAsync_ParsesFormEncodedTokenResponse()
    {
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "access_token=form-access-token&refresh_token=form-refresh-token&expires_in=3600",
                        Encoding.UTF8,
                        "application/x-www-form-urlencoded")
                }));

        var client = new GmailInboxClient(
            httpClient,
            Options.Create(
                new GmailOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }));

        var result = await client.ExchangeCodeAsync(
            "auth-code",
            "code-verifier",
            "http://localhost:5114/api/inbox-accounts/gmail/oauth/callback",
            CancellationToken.None);

        Assert.Equal("form-access-token", result.AccessToken);
        Assert.Equal("form-refresh-token", result.RefreshToken);
        Assert.True(result.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(59));
    }

    [Fact]
    public async Task SearchRecruitingEmailsAsync_PaginatesAndSkipsExistingMessageIds()
    {
        var requestedMessageIds = new List<string>();

        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                request =>
                {
                    var uri = request.RequestUri!;

                    if (uri.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
                    {
                        var query = QueryHelpers.ParseQuery(uri.Query);
                        var search = query.TryGetValue("q", out var q) ? q.ToString() : string.Empty;
                        var pageToken = query.TryGetValue("pageToken", out var token)
                            ? token.ToString()
                            : null;

                        var payload = (search, pageToken) switch
                        {
                            ("\"thank you for applying\"", null) =>
                                """
                                {
                                  "messages": [
                                    { "id": "job-1" },
                                    { "id": "job-2" }
                                  ],
                                  "nextPageToken": "page-2"
                                }
                                """,
                            ("\"thank you for applying\"", "page-2") =>
                                """
                                {
                                  "messages": [
                                    { "id": "job-3" }
                                  ]
                                }
                                """,
                            ("recruiter", null) =>
                                """
                                {
                                  "messages": [
                                    { "id": "job-2" },
                                    { "id": "job-4" }
                                  ]
                                }
                                """,
                            ("offer", null) =>
                                """
                                {
                                  "messages": [
                                    { "id": "job-5" },
                                    { "id": "job-6" }
                                  ]
                                }
                                """,
                            _ => """{ "messages": [] }"""
                        };

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(payload, Encoding.UTF8, "application/json")
                        };
                    }

                    if (uri.AbsolutePath.Contains("/threads/", StringComparison.Ordinal))
                    {
                        var threadId = uri.AbsolutePath.Split('/').Last();
                        var messageId = threadId.StartsWith("thread-", StringComparison.Ordinal)
                            ? threadId["thread-".Length..]
                            : threadId;

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                $$"""
                                  {
                                    "messages": [
                                      {{BuildMessageJson(messageId)}}
                                    ]
                                  }
                                """,
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    if (uri.AbsolutePath.Contains("/messages/", StringComparison.Ordinal))
                    {
                        var messageId = uri.AbsolutePath.Split('/').Last();
                        requestedMessageIds.Add(messageId);

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                BuildMessageJson(messageId),
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }));

        var client = new GmailInboxClient(
            httpClient,
            Options.Create(
                new GmailOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }));

        var results = await client.SearchRecruitingEmailsAsync(
            "access-token",
            existingMessageIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "job-1"
            },
            cancellationToken: CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.DoesNotContain("job-1", requestedMessageIds);
        Assert.Contains("job-2", requestedMessageIds);
        Assert.Contains("job-3", requestedMessageIds);
        Assert.Contains("job-4", requestedMessageIds);
        Assert.Contains("job-5", requestedMessageIds);
        Assert.Contains("job-6", requestedMessageIds);
        Assert.Equal(5, requestedMessageIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(results, email => email.ExternalMessageId == "job-2");
        Assert.Contains(results, email => email.ExternalMessageId == "job-3");
        Assert.Contains(results, email => email.ExternalMessageId == "job-4");
        Assert.DoesNotContain(results, email => email.ExternalMessageId == "job-5");
        Assert.DoesNotContain(results, email => email.ExternalMessageId == "job-6");
    }

    [Fact]
    public async Task SearchRecruitingEmailsAsync_IncludesSentRepliesFromRecruitingThreads()
    {
        var requestedThreadIds = new List<string>();

        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                request =>
                {
                    var uri = request.RequestUri!;

                    if (uri.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
                    {
                        var query = QueryHelpers.ParseQuery(uri.Query);
                        var search = query.TryGetValue("q", out var q) ? q.ToString() : string.Empty;
                        var payload = search == "interview"
                            ? """{ "messages": [ { "id": "inbound-1" } ] }"""
                            : """{ "messages": [] }""";

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(payload, Encoding.UTF8, "application/json")
                        };
                    }

                    if (uri.AbsolutePath.Contains("/threads/", StringComparison.Ordinal))
                    {
                        var threadId = uri.AbsolutePath.Split('/').Last();
                        requestedThreadIds.Add(threadId);

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                $$"""
                                  {
                                    "messages": [
                                      {{BuildCustomMessageJson(
                                          "inbound-1",
                                          "thread-veeva",
                                          "Recruiter <recruiting@veeva.com>",
                                          "Interview availability",
                                          "Please reply with your availability.",
                                          isSent: false)}},
                                      {{BuildCustomMessageJson(
                                          "sent-1",
                                          "thread-veeva",
                                          "Jordan Lee <jlee@example.edu>",
                                          "Re: Interview availability",
                                          "Thanks, I am available Tuesday afternoon.",
                                          isSent: true)}}
                                    ]
                                  }
                                """,
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    if (uri.AbsolutePath.Contains("/messages/", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                BuildCustomMessageJson(
                                    "inbound-1",
                                    "thread-veeva",
                                    "Recruiter <recruiting@veeva.com>",
                                    "Interview availability",
                                    "Please reply with your availability.",
                                    isSent: false),
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }));

        var client = new GmailInboxClient(
            httpClient,
            Options.Create(
                new GmailOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }));

        var results = await client.SearchRecruitingEmailsAsync(
            "access-token",
            existingMessageIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            mailboxEmailAddress: "jlee@example.edu",
            cancellationToken: CancellationToken.None);

        Assert.Contains("thread-veeva", requestedThreadIds);
        Assert.Contains(results, email => email.ExternalMessageId == "inbound-1" && !email.IsOutbound);

        var sentReply = Assert.Single(results, email => email.ExternalMessageId == "sent-1");
        Assert.True(sentReply.IsOutbound);
        Assert.Equal("thread-veeva", sentReply.ThreadId);
        Assert.Contains("Tuesday afternoon", sentReply.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchRecruitingEmailsAsync_FindsRecentSentRepliesForKnownThreads()
    {
        var sentSearches = new List<string>();

        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                request =>
                {
                    var uri = request.RequestUri!;

                    if (uri.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
                    {
                        var query = QueryHelpers.ParseQuery(uri.Query);
                        var search = query.TryGetValue("q", out var q) ? q.ToString() : string.Empty;
                        if (search.StartsWith("in:sent", StringComparison.OrdinalIgnoreCase))
                        {
                            sentSearches.Add(search);
                            return new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent(
                                    """{ "messages": [ { "id": "sent-1" } ] }""",
                                    Encoding.UTF8,
                                    "application/json")
                            };
                        }

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("""{ "messages": [] }""", Encoding.UTF8, "application/json")
                        };
                    }

                    if (uri.AbsolutePath.Contains("/messages/", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                BuildCustomMessageJson(
                                    "sent-1",
                                    "thread-veeva",
                                    "Jordan Lee <jlee@example.edu>",
                                    "Re: Interview availability",
                                    "Thanks, I already replied with availability.",
                                    isSent: true),
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }));

        var client = new GmailInboxClient(
            httpClient,
            Options.Create(
                new GmailOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }));

        var results = await client.SearchRecruitingEmailsAsync(
            "access-token",
            existingMessageIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "inbound-1"
            },
            existingThreadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "thread-veeva"
            },
            receivedAfterUtc: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            sentAfterUtc: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            mailboxEmailAddress: "jlee@example.edu",
            cancellationToken: CancellationToken.None);

        var sentReply = Assert.Single(results);
        Assert.Contains("in:sent after:2026/04/01", sentSearches);
        Assert.True(sentReply.IsOutbound);
        Assert.Equal("sent-1", sentReply.ExternalMessageId);
        Assert.Equal("thread-veeva", sentReply.ThreadId);
    }

    [Fact]
    public async Task GetIncrementalRecruitingEmailsAsync_ReturnsCurrentHistoryIdAndNewMessages()
    {
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                request =>
                {
                    var uri = request.RequestUri!;

                    if (uri.AbsolutePath.EndsWith("/history", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                """
                                {
                                  "history": [
                                    {
                                      "id": "history-101",
                                      "messagesAdded": [
                                        { "message": { "id": "inbound-2", "threadId": "thread-veeva" } },
                                        { "message": { "id": "sent-2", "threadId": "thread-veeva" } }
                                      ]
                                    }
                                  ],
                                  "historyId": "history-200"
                                }
                                """,
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    if (uri.AbsolutePath.Contains("/messages/", StringComparison.Ordinal))
                    {
                        var messageId = uri.AbsolutePath.Split('/').Last();
                        var payload = messageId switch
                        {
                            "inbound-2" => BuildCustomMessageJson(
                                "inbound-2",
                                "thread-veeva",
                                "Recruiter <recruiting@veeva.com>",
                                "Interview availability",
                                "Please choose a slot.",
                                isSent: false),
                            "sent-2" => BuildCustomMessageJson(
                                "sent-2",
                                "thread-veeva",
                                "Jordan Lee <jlee@example.edu>",
                                "Re: Interview availability",
                                "Tuesday afternoon works.",
                                isSent: true),
                            _ => throw new InvalidOperationException($"Unexpected message {messageId}.")
                        };

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(payload, Encoding.UTF8, "application/json")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }));

        var client = new GmailInboxClient(
            httpClient,
            Options.Create(
                new GmailOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }));

        var result = await client.GetIncrementalRecruitingEmailsAsync(
            "access-token",
            "history-100",
            existingThreadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "thread-veeva"
            },
            mailboxEmailAddress: "jlee@example.edu",
            cancellationToken: CancellationToken.None);

        Assert.False(result.RequiresFullSyncFallback);
        Assert.Equal("history-200", result.HistoryId);
        Assert.Equal(2, result.Emails.Count);
        Assert.Contains(result.Emails, email => email.ExternalMessageId == "inbound-2");
        Assert.Contains(result.Emails, email => email.ExternalMessageId == "sent-2" && email.IsOutbound);
    }

    [Fact]
    public async Task GetIncrementalRecruitingEmailsAsync_SkipsMessagesThatDisappearBeforeFetch()
    {
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                request =>
                {
                    var uri = request.RequestUri!;

                    if (uri.AbsolutePath.EndsWith("/history", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                """
                                {
                                  "history": [
                                    {
                                      "id": "history-101",
                                      "messagesAdded": [
                                        { "message": { "id": "vanished-1", "threadId": "thread-veeva" } },
                                        { "message": { "id": "inbound-2", "threadId": "thread-veeva" } }
                                      ]
                                    }
                                  ],
                                  "historyId": "history-201"
                                }
                                """,
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    if (uri.AbsolutePath.Contains("/messages/vanished-1", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }

                    if (uri.AbsolutePath.Contains("/messages/inbound-2", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                BuildCustomMessageJson(
                                    "inbound-2",
                                    "thread-veeva",
                                    "Recruiter <recruiting@veeva.com>",
                                    "Interview availability",
                                    "Please choose a slot.",
                                    isSent: false),
                                Encoding.UTF8,
                                "application/json")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }));

        var client = new GmailInboxClient(
            httpClient,
            Options.Create(
                new GmailOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }));

        var result = await client.GetIncrementalRecruitingEmailsAsync(
            "access-token",
            "history-100",
            existingThreadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "thread-veeva"
            },
            cancellationToken: CancellationToken.None);

        Assert.False(result.RequiresFullSyncFallback);
        Assert.Equal("history-201", result.HistoryId);
        var email = Assert.Single(result.Emails);
        Assert.Equal("inbound-2", email.ExternalMessageId);
    }

    [Fact]
    public async Task GetIncrementalRecruitingEmailsAsync_ReturnsFullSyncFallback_WhenHistoryCursorExpired()
    {
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        """
                        {
                          "error": {
                            "code": 404,
                            "message": "Requested entity was not found."
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                }));

        var client = new GmailInboxClient(
            httpClient,
            Options.Create(
                new GmailOptions
                {
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }));

        var result = await client.GetIncrementalRecruitingEmailsAsync(
            "access-token",
            "history-stale",
            cancellationToken: CancellationToken.None);

        Assert.True(result.RequiresFullSyncFallback);
        Assert.Null(result.HistoryId);
        Assert.Empty(result.Emails);
    }

    private static string BuildMessageJson(string messageId)
    {
        var subject = messageId switch
        {
            "job-2" => "We received your application for Software Engineer",
            "job-3" => "Interview invitation",
            "job-4" => "Recruiter outreach from Stripe",
            "job-5" => "6 months of DashPass, on us",
            "job-6" => "Don't ghost this opportunity!",
            _ => "(unknown)"
        };

        var sender = messageId switch
        {
            "job-2" => "careers@example.com",
            "job-3" => "interviews@example.com",
            "job-4" => "recruiter@example.com",
            "job-5" => "noreply@robinhood.com",
            "job-6" => "handshake@notifications.joinhandshake.com",
            _ => "unknown@example.com"
        };

        var body = messageId switch
        {
            "job-4" => "I came across your background and would love to connect about the software engineer role at Stripe.",
            "job-5" => "Activate DashPass rewards now and view your account statement.",
            "job-6" => "There is still time to register for tomorrow's BT Career Webinar. Join virtually from anywhere and register here.",
            _ => "Test body"
        };

        return BuildCustomMessageJson(
            messageId,
            $"thread-{messageId}",
            sender,
            subject,
            body,
            isSent: false);
    }

    private static string BuildCustomMessageJson(
        string messageId,
        string threadId,
        string sender,
        string subject,
        string body,
        bool isSent)
    {
        var labelIdsJson = isSent ? """["SENT"]""" : "[]";

        return $$"""
          {
            "id": {{JsonSerializer.Serialize(messageId)}},
            "threadId": {{JsonSerializer.Serialize(threadId)}},
            "internalDate": "1775491200000",
            "snippet": {{JsonSerializer.Serialize(subject)}},
            "labelIds": {{labelIdsJson}},
            "payload": {
              "mimeType": "text/plain",
              "headers": [
                { "name": "From", "value": {{JsonSerializer.Serialize(sender)}} },
                { "name": "Subject", "value": {{JsonSerializer.Serialize(subject)}} }
              ],
              "body": {
                "data": "{{EncodeBase64Url(body)}}"
              }
            }
          }
        """;
    }

    private static string EncodeBase64Url(string value)
    {
        return EncodeBase64Url(Encoding.UTF8.GetBytes(value));
    }

    private static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handle;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
        {
            _handle = handle;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handle(request));
        }
    }
}
