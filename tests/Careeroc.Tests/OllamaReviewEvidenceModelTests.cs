using System.Net;
using System.Net.Http;
using System.Text;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Careeroc.Tests;

public sealed class OllamaReviewEvidenceModelTests
{
    [Fact]
    public async Task EvaluateAsync_ReturnsGemmaReviewDecision_WhenConfigured()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(
            async request =>
            {
                requestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync();

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "role": "assistant",
                            "content": "{\"requiresManualReview\":true,\"reason\":\"Application match is too ambiguous for an offer.\",\"visibleOutput\":\"Gemma review agent escalated the offer evidence.\",\"confidence\":0.87}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            });
        var model = new OllamaReviewEvidenceModel(
            new HttpClient(handler),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b"
                }),
            OllamaModelCircuitBreaker.Disabled,
            new StubAgentContextSnapshotProvider(
                "user@example.com",
                """
                # memory.md

                ## preference
                - target_role: backend platform intern
                """,
                """
                # soul.md

                ## Voice
                - Narrative tone: direct
                """),
            NullLogger<OllamaReviewEvidenceModel>.Instance);

        var decision = await model.EvaluateAsync(
            BuildParsed(),
            BuildMatch(0.48m),
            new ReviewEvidenceDecision(
                true,
                "Low application-match confidence (0.48).",
                "Escalated with thread evidence because the workflow state is not yet trustworthy.",
                "Review Evidence Agent"),
            modelCallContext: new ModelCallContext("user@example.com", null, null, "review-run"));

        Assert.NotNull(decision);
        Assert.True(decision.RequiresManualReview);
        Assert.Equal(0.87m, decision.Confidence);
        Assert.Equal("Review Evidence Agent: Gemma via Ollama (gemma4:26b)", decision.AgentName);
        Assert.Contains("Review Evidence Agent, a second-pass reviewer", requestBody, StringComparison.Ordinal);
        Assert.Contains("Application match:", requestBody, StringComparison.Ordinal);
        Assert.Contains("Trusted user context from DB-backed memory", requestBody, StringComparison.Ordinal);
        Assert.Contains("backend platform intern", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_RecordsModelCallEvent_WithUsageAndContext()
    {
        var inboxAccountId = Guid.NewGuid();
        var inboxEmailId = Guid.NewGuid();
        var recorder = new RecordingModelCallEventRecorder();
        var handler = new StubHttpMessageHandler(
            _ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "role": "assistant",
                            "content": "{\"requiresManualReview\":false,\"reason\":\"\",\"visibleOutput\":\"Gemma review agent cleared the evidence.\",\"confidence\":0.91}"
                          },
                          "prompt_eval_count": 100,
                          "eval_count": 25
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                }));
        var model = new OllamaReviewEvidenceModel(
            new HttpClient(handler),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b",
                    PromptTokenCostUsdPer1K = 0.01m,
                    CompletionTokenCostUsdPer1K = 0.02m
                }),
            OllamaModelCircuitBreaker.Disabled,
            recorder,
            NullLogger<OllamaReviewEvidenceModel>.Instance);

        await model.EvaluateAsync(
            BuildParsed(),
            BuildMatch(0.48m),
            new ReviewEvidenceDecision(
                true,
                "Low application-match confidence (0.48).",
                "Escalated with thread evidence because the workflow state is not yet trustworthy.",
                "Review Evidence Agent"),
            modelCallContext: new ModelCallContext(
                "user@example.com",
                inboxAccountId,
                inboxEmailId,
                "email-run"));

        var record = Assert.Single(recorder.Records);
        Assert.Equal("ollama", record.Provider);
        Assert.Equal("review_evidence", record.Purpose);
        Assert.Equal("gemma4:26b", record.Model);
        Assert.Equal("success", record.Outcome);
        Assert.Equal("user@example.com", record.UserEmail);
        Assert.Equal(inboxAccountId, record.InboxAccountId);
        Assert.Equal(inboxEmailId, record.InboxEmailId);
        Assert.Equal("email-run", record.RunId);
        Assert.Equal(100, record.PromptTokens);
        Assert.Equal(25, record.CompletionTokens);
        Assert.Equal(125, record.TotalTokens);
        Assert.Equal(0.001500m, record.EstimatedCostUsd);
        Assert.Equal(200, record.StatusCode);
        Assert.True(record.LatencyMs >= 0);
    }

    [Fact]
    public async Task EvaluateAsync_UsesPrimaryModel_ForBaselineEscalationsByDefault()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(
            async request =>
            {
                requestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync();

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "role": "assistant",
                            "content": "{\"requiresManualReview\":false,\"reason\":\"\",\"visibleOutput\":\"Gemma review agent cleared the evidence.\",\"confidence\":0.91}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            });
        var model = new OllamaReviewEvidenceModel(
            new HttpClient(handler),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "https://ollama.com",
                    Model = "gemma4:26b"
                }),
            NullLogger<OllamaReviewEvidenceModel>.Instance);

        await model.EvaluateAsync(
            BuildParsed(),
            BuildMatch(0.42m),
            new ReviewEvidenceDecision(
                true,
                "Low application-match confidence (0.42).",
                "Escalated with thread evidence because the workflow state is not yet trustworthy.",
                "Review Evidence Agent"));

        Assert.NotNull(requestBody);
        Assert.Contains("\"model\":\"gemma4:26b\"", requestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("gemma4:31b", requestBody, StringComparison.Ordinal);
    }

    private static ParsedRecruitingEmail BuildParsed()
    {
        return new ParsedRecruitingEmail(
            EmailCategory.Offer,
            ApplicationStatus.Offer,
            "Figma",
            "Product Engineer",
            ActionRequired: true,
            DueDateUtc: null,
            ClassificationConfidence: 0.92m,
            WorkflowNote: "Offer email with a deadline.",
            ParsingSource: "Gemma via Ollama",
            ParsingModel: "gemma4:26b");
    }

    private static ApplicationMatchResult BuildMatch(decimal confidence)
    {
        return new ApplicationMatchResult(
            new JobApplication
            {
                Company = "Figma",
                Role = "Product Engineer",
                Status = ApplicationStatus.Offer
            },
            Created: false,
            MatchingConfidence: confidence,
            ReviewReason: "Could not confidently map this email to an existing application.");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return await responseFactory(request);
        }
    }

    private sealed class RecordingModelCallEventRecorder : IModelCallEventRecorder
    {
        public List<ModelCallEventRecord> Records { get; } = [];

        public Task RecordAsync(ModelCallEventRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class StubAgentContextSnapshotProvider(
        string expectedUserEmail,
        string memoryMarkdown,
        string soulMarkdown) : IUserAgentContextSnapshotProvider
    {
        public Task<UserAgentContextSnapshotDto> GetSnapshotAsync(
            string? userEmail,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedUserEmail, userEmail);
            return Task.FromResult(
                new UserAgentContextSnapshotDto(
                    DateTimeOffset.UtcNow,
                    memoryMarkdown,
                    soulMarkdown,
                    1,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow));
        }
    }
}
