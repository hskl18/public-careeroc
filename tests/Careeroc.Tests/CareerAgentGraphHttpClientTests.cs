using System.Net;
using System.Text;
using System.Text.Json;
using Careeroc.Application.Models;
using Careeroc.Infrastructure.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Careeroc.Tests;

public sealed class CareerAgentGraphHttpClientTests
{
    [Fact]
    public async Task ReviewResumeMailboxAsync_ReturnsTypedResponseAndSendsSharedSecret()
    {
        string? sharedSecret = null;
        var handler = new StubHttpMessageHandler(
            request =>
            {
                sharedSecret = request.Headers.GetValues("X-Careeroc-Agent-Secret").Single();
                var response = new CareerAgentGraphReviewResponse(
                    "resume-mailbox:test-001",
                    "completed",
                    "langgraph.resume_mailbox_review.v1",
                    "ollama/gemma4:31b-cloud",
                    "Graph summary",
                    ["Finding"],
                    ["Action"],
                    false,
                    0.91m,
                    []);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                        Encoding.UTF8,
                        "application/json")
                };
            });
        var client = CreateClient(handler, sharedSecret: "test-secret");

        var outcome = await client.ReviewResumeMailboxAsync(BuildRequest("resume-mailbox:test-001"));

        Assert.True(outcome.Attempted);
        Assert.NotNull(outcome.Response);
        Assert.Equal("Graph summary", outcome.Response.Summary);
        Assert.Equal("test-secret", sharedSecret);
    }

    [Fact]
    public async Task ReviewResumeMailboxAsync_RejectsMismatchedThreadId()
    {
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "threadId": "resume-mailbox:another-run",
                      "status": "completed",
                      "runtimePath": "langgraph.resume_mailbox_review.v1",
                      "modelPath": "fake/model",
                      "summary": "Wrong run",
                      "findings": [],
                      "nextActions": [],
                      "requiresHumanReview": false,
                      "confidence": 0.9,
                      "trace": []
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        var client = CreateClient(handler);

        var outcome = await client.ReviewResumeMailboxAsync(BuildRequest("resume-mailbox:test-002"));

        Assert.True(outcome.Attempted);
        Assert.Null(outcome.Response);
        Assert.Equal("Agent runtime returned an invalid or mismatched response.", outcome.FailureReason);
    }

    private static CareerAgentGraphHttpClient CreateClient(
        HttpMessageHandler handler,
        string sharedSecret = "")
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://agent-runtime")
        };
        return new CareerAgentGraphHttpClient(
            httpClient,
            Options.Create(
                new AgentRuntimeOptions
                {
                    Enabled = true,
                    BaseUrl = "http://agent-runtime",
                    SharedSecret = sharedSecret
                }),
            NullLogger<CareerAgentGraphHttpClient>.Instance);
    }

    private static CareerAgentGraphReviewRequest BuildRequest(string threadId)
    {
        return new CareerAgentGraphReviewRequest(
            threadId,
            new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero),
            "Baseline summary",
            [],
            [],
            new CareerAgentGraphEvidence(0, 0, 0, 0, 0, null),
            null,
            [],
            [],
            [],
            [],
            [],
            []);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
