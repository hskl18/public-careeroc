using System.Net;
using System.Net.Http;
using System.Text;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Careeroc.Tests;

public sealed class OllamaRecruitingEmailParserTests
{
    [Fact]
    public async Task ParseAsync_UsesOllamaExtraction_WhenConfigured()
    {
        var handler = new StubHttpMessageHandler(
            _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "role": "assistant",
                            "content": "{\"category\":\"interview_invitation\",\"recommendedStatus\":\"Interviewing\",\"company\":\"OpenAI\",\"role\":\"Research Engineer\",\"actionRequired\":true,\"dueDateUtc\":\"2026-04-12T00:00:00Z\",\"classificationConfidence\":0.92,\"workflowNote\":\"Detected interview scheduling for OpenAI / Research Engineer.\"}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                });
        var client = new HttpClient(handler);
        var parser = new OllamaRecruitingEmailParser(
            client,
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b",
                    MinimumConfidence = 0.60m
                }),
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        var parsed = await parser.ParseAsync(
            new InboxEmail
            {
                Sender = "recruiting@openai.com",
                Subject = "Interview availability for Research Engineer",
                ParsedBody = "We would love to schedule your interview for the Research Engineer role at OpenAI. Please share your availability by April 12, 2026."
            });

        Assert.Equal(EmailCategory.InterviewInvitation, parsed.Category);
        Assert.Equal(ApplicationStatus.Interviewing, parsed.RecommendedStatus);
        Assert.Equal("OpenAI", parsed.Company);
        Assert.Equal("Research Engineer", parsed.Role);
        Assert.True(parsed.ActionRequired);
        Assert.Equal(0.92m, parsed.ClassificationConfidence);
    }

    [Fact]
    public async Task ParseAsync_DoesNotLetModelSuppressCandidateQuestionnaireAction()
    {
        var handler = new StubHttpMessageHandler(
            _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "role": "assistant",
                            "content": "{\"category\":\"oa_invitation\",\"recommendedStatus\":\"OaPending\",\"company\":\"Google\",\"role\":\"Student Researcher Program\",\"actionRequired\":false,\"dueDateUtc\":\"\",\"classificationConfidence\":0.93,\"workflowNote\":\"Candidate questionnaire was detected.\"}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                });
        var parser = new OllamaRecruitingEmailParser(
            new HttpClient(handler),
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b",
                    MinimumConfidence = 0.60m
                }),
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        var parsed = await parser.ParseAsync(
            new InboxEmail
            {
                Sender = "Google Recruiting Program (no reply) <google-recruiting-noreply@google.com>",
                Subject = "Candidate questionnaire: Jordan Lee",
                ParsedBody = "Thank you for your interest in the Student Researcher Program at Google! Please complete the candidate questionnaire as soon as possible. Your profile will not be shared until this form is submitted.",
                ReceivedAtUtc = new DateTimeOffset(2026, 4, 23, 7, 14, 0, TimeSpan.Zero)
            });

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
        Assert.Equal("Google", parsed.Company);
        Assert.Equal("Student Researcher Program", parsed.Role);
        Assert.True(parsed.ActionRequired);
        Assert.Null(parsed.DueDateUtc);
    }

    [Fact]
    public async Task ParseAsync_FallsBackToHeuristicCompany_WhenModelReturnsProtocolToken()
    {
        var handler = new StubHttpMessageHandler(
            _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "role": "assistant",
                            "content": "{\"category\":\"oa_invitation\",\"recommendedStatus\":\"OaPending\",\"company\":\"https\",\"role\":\"Software Intern\",\"actionRequired\":true,\"dueDateUtc\":\"\",\"classificationConfidence\":0.91,\"workflowNote\":\"Assessment link detected.\"}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                });
        var parser = new OllamaRecruitingEmailParser(
            new HttpClient(handler),
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b",
                    MinimumConfidence = 0.60m
                }),
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        var parsed = await parser.ParseAsync(
            new InboxEmail
            {
                Sender = "HackerRank <no-reply@hackerrankmail.com>",
                Subject = "Bot Auto Software Intern Test - Infra",
                ParsedBody = """
                    Powered by HackerRank
                    Thank you for your interest in joining Bot Auto! We're excited to invite you to the next step in the process: the HackerRank Bot Auto Software Intern Test - Infra assessment.
                    End Login Date/Time: 3 May 2026 09:00 AM PDT (America - Los Angeles)
                    """
            });

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
        Assert.Equal("Bot Auto", parsed.Company);
        Assert.Equal("Software Intern", parsed.Role);
        Assert.Equal(new DateTimeOffset(2026, 5, 3, 16, 0, 0, TimeSpan.Zero), parsed.DueDateUtc);
    }

    [Fact]
    public async Task ParseAsync_RecordsModelCallEvent_WhenOllamaRuns()
    {
        var inboxAccountId = Guid.NewGuid();
        var inboxEmailId = Guid.NewGuid();
        var recorder = new RecordingModelCallEventRecorder();
        var handler = new StubHttpMessageHandler(
            _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "role": "assistant",
                            "content": "{\"category\":\"interview_invitation\",\"recommendedStatus\":\"Interviewing\",\"company\":\"OpenAI\",\"role\":\"Research Engineer\",\"actionRequired\":true,\"dueDateUtc\":\"2026-04-12T00:00:00Z\",\"classificationConfidence\":0.92,\"workflowNote\":\"Detected interview scheduling.\"}"
                          },
                          "prompt_eval_count": 12,
                          "eval_count": 7
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                });
        var parser = new OllamaRecruitingEmailParser(
            new HttpClient(handler),
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b",
                    MinimumConfidence = 0.60m
                }),
            NullRecruitingEmailGuidanceStore.Instance,
            OllamaModelCircuitBreaker.Disabled,
            recorder,
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        await parser.ParseAsync(
            new InboxEmail
            {
                Id = inboxEmailId,
                InboxAccountId = inboxAccountId,
                InboxAccount = new InboxAccount
                {
                    Id = inboxAccountId,
                    EmailAddress = "candidate@example.com",
                    User = new User { Email = "Candidate@Example.com" }
                },
                Sender = "recruiting@openai.com",
                Subject = "Interview availability for Research Engineer",
                ParsedBody = "We would love to schedule your interview for the Research Engineer role at OpenAI."
            });

        var record = Assert.Single(recorder.Records);
        Assert.Equal("ollama", record.Provider);
        Assert.Equal("workflow_extraction", record.Purpose);
        Assert.Equal("gemma4:26b", record.Model);
        Assert.Equal("success", record.Outcome);
        Assert.Equal("candidate@example.com", record.UserEmail);
        Assert.Equal(inboxAccountId, record.InboxAccountId);
        Assert.Equal(inboxEmailId, record.InboxEmailId);
        Assert.Equal(inboxEmailId.ToString("N"), record.RunId);
        Assert.Equal(12, record.PromptTokens);
        Assert.Equal(7, record.CompletionTokens);
        Assert.Equal(19, record.TotalTokens);
        Assert.Equal(0m, record.EstimatedCostUsd);
        Assert.Equal(200, record.StatusCode);
        Assert.True(record.LatencyMs >= 0);
    }

    [Fact]
    public async Task ParseAsync_RecordsModelCallEvent_WhenOllamaHttpFails()
    {
        var recorder = new RecordingModelCallEventRecorder();
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var parser = new OllamaRecruitingEmailParser(
            new HttpClient(handler),
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b"
                }),
            NullRecruitingEmailGuidanceStore.Instance,
            OllamaModelCircuitBreaker.Disabled,
            recorder,
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        await parser.ParseAsync(
            new InboxEmail
            {
                Sender = "recruiting@ramp.com",
                Subject = "Ramp Online Assessment for Software Engineer",
                ParsedBody = "Please complete the online assessment for the Software Engineer role by April 10, 2026."
            });

        var record = Assert.Single(recorder.Records);
        Assert.Equal("http_error", record.Outcome);
        Assert.Equal(503, record.StatusCode);
        Assert.Equal("http_503", record.ErrorType);
    }

    [Fact]
    public async Task ParseAsync_IncludesGuidancePlaybook_InOllamaPrompt()
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
                            "content": "{\"category\":\"generic_update\",\"recommendedStatus\":\"Applied\",\"company\":\"CIBC\",\"role\":\"2026 Summer Intern - Software Engineer\",\"actionRequired\":false,\"dueDateUtc\":\"\",\"classificationConfidence\":0.81,\"workflowNote\":\"Workday digest does not expose a concrete assessment task.\"}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            });
        var guidanceStore = new StubGuidanceStore(
            "Workday daily digests are generic_update unless the body names an actual assessment task.");
        var agentContextProvider = new StubAgentContextSnapshotProvider(
            "student@example.com",
            """
            # memory.md

            ## preference
            - target_stack: full-stack TypeScript
            """,
            """
            # soul.md

            ## Voice
            - Narrative tone: concise and evidence-backed
            """);
        var parser = new OllamaRecruitingEmailParser(
            new HttpClient(handler),
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b"
                }),
            guidanceStore,
            agentContextProvider,
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        await parser.ParseAsync(
            new InboxEmail
            {
                InboxAccount = new InboxAccount
                {
                    EmailAddress = "student@example.com"
                },
                Sender = "mailbox.cibc-workdayactionrequired@cibc.com",
                Subject = "Workday Inbox - Your Daily Digest",
                ParsedBody = "Your application for the 2026 Summer Intern Software Engineer position at CIBC has an inbox item due April 16, 2026."
            });

        Assert.NotNull(requestBody);
        Assert.Contains("Recruiting email guidance playbook", requestBody, StringComparison.Ordinal);
        Assert.Contains("Workday daily digests are generic_update", requestBody, StringComparison.Ordinal);
        Assert.Contains("Trusted user context from DB-backed memory", requestBody, StringComparison.Ordinal);
        Assert.Contains("target_stack", requestBody, StringComparison.Ordinal);
        Assert.Contains("Narrative tone", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseAsync_FallsBackToHeuristics_WhenOllamaFails()
    {
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new HttpClient(handler);
        var parser = new OllamaRecruitingEmailParser(
            client,
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b"
                }),
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        var parsed = await parser.ParseAsync(
            new InboxEmail
            {
                Sender = "recruiting@ramp.com",
                Subject = "Ramp Online Assessment for Software Engineer",
                ParsedBody = "Please complete the online assessment for the Software Engineer role by April 10, 2026."
            });

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
        Assert.Equal(ApplicationStatus.OaPending, parsed.RecommendedStatus);
        Assert.Equal("Ramp", parsed.Company);
        Assert.Equal("Software Engineer", parsed.Role);
        Assert.True(parsed.ActionRequired);
    }

    [Fact]
    public async Task ParseAsync_StripsCloudSuffix_WhenCallingOllamaDotComDirectly()
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
                            "content": "{\"category\":\"application_received\",\"recommendedStatus\":\"Applied\",\"company\":\"OpenAI\",\"role\":\"Research Engineer\",\"actionRequired\":false,\"dueDateUtc\":\"\",\"classificationConfidence\":0.90,\"workflowNote\":\"Detected application receipt.\"}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            });
        var client = new HttpClient(handler);
        var parser = new OllamaRecruitingEmailParser(
            client,
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "https://ollama.com",
                    Model = "gemma4:26b"
                }),
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        await parser.ParseAsync(
            new InboxEmail
            {
                Sender = "careers@openai.com",
                Subject = "Application received",
                ParsedBody = "We received your application for Research Engineer at OpenAI."
            });

        Assert.NotNull(requestBody);
        Assert.Contains("\"model\":\"gemma4:26b\"", requestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("model-router-fallback", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseAsync_SkipsOllama_WhenCircuitBreakerIsOpen()
    {
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(
            _ =>
            {
                requestCount++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });
        var options = Options.Create(
            new OllamaEmailExtractionOptions
            {
                Enabled = true,
                BaseUrl = "http://localhost:11434",
                Model = "gemma4:26b",
                FailureCircuitBreakerThreshold = 1,
                FailureCircuitBreakerCooldownSeconds = 60
            });
        var circuitBreaker = new OllamaModelCircuitBreaker(
            new TestClock(new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero)));
        var parser = new OllamaRecruitingEmailParser(
            new HttpClient(handler),
            new HeuristicRecruitingEmailParser(),
            options,
            NullRecruitingEmailGuidanceStore.Instance,
            circuitBreaker,
            NullLogger<OllamaRecruitingEmailParser>.Instance);
        var email = new InboxEmail
        {
            Sender = "recruiting@ramp.com",
            Subject = "Ramp Online Assessment for Software Engineer",
            ParsedBody = "Please complete the online assessment for the Software Engineer role by April 10, 2026."
        };

        await parser.ParseAsync(email);
        await parser.ParseAsync(email);

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task ParseAsync_UsesPrimaryModel_ForOfferEmails()
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
                            "content": "{\"category\":\"offer\",\"recommendedStatus\":\"Offer\",\"company\":\"Figma\",\"role\":\"Product Engineer\",\"actionRequired\":true,\"dueDateUtc\":\"2026-04-20T00:00:00Z\",\"classificationConfidence\":0.96,\"workflowNote\":\"Detected an offer with an action deadline.\"}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            });
        var parser = new OllamaRecruitingEmailParser(
            new HttpClient(handler),
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b"
                }),
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        await parser.ParseAsync(
            new InboxEmail
            {
                Sender = "recruiting@figma.com",
                Subject = "Your offer",
                ParsedBody = "We are excited to extend you an offer for the Product Engineer role at Figma. Please reply by April 20, 2026."
            });

        Assert.NotNull(requestBody);
        Assert.Contains("\"model\":\"gemma4:26b\"", requestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseAsync_PreservesHeuristicOfferPaperworkFallback_WhenModelReturnsUnknownRole()
    {
        var handler = new StubHttpMessageHandler(
            _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "role": "assistant",
                            "content": "{\"category\":\"offer\",\"recommendedStatus\":\"Offer\",\"company\":\"Northgate Physics HR/AP\",\"role\":\"Unknown Role\",\"actionRequired\":true,\"dueDateUtc\":\"\",\"classificationConfidence\":0.95,\"workflowNote\":\"Detected offer letter paperwork.\"}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                });
        var parser = new OllamaRecruitingEmailParser(
            new HttpClient(handler),
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b"
                }),
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        var parsed = await parser.ParseAsync(
            new InboxEmail
            {
                Sender = "\"Northgate Physics HR/AP\" <hr-ap-phys@example.edu>",
                Subject = "ACTION REQUIRED: Signature on Offer Letter and Hire Paperwork Via Onboarding Solutions",
                ParsedBody = "We have not received your signed Offer Letter or the hire documents sent via Onboarding Solutions."
            });

        Assert.Equal(EmailCategory.Offer, parsed.Category);
        Assert.Equal("Northgate Physics", parsed.Company);
        Assert.Equal("Offer Letter / Hire Paperwork", parsed.Role);
    }

    [Fact]
    public async Task ParseAsync_RejectsBareCampusCompany_WhenMailboxSharesInstitutionalDomain()
    {
        var handler = new StubHttpMessageHandler(
            _ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "role": "assistant",
                            "content": "{\"category\":\"application_received\",\"recommendedStatus\":\"Applied\",\"company\":\"Northgate\",\"role\":\"AI Intern\",\"actionRequired\":false,\"dueDateUtc\":\"\",\"classificationConfidence\":0.91,\"workflowNote\":\"Detected application receipt.\"}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                });
        var parser = new OllamaRecruitingEmailParser(
            new HttpClient(handler),
            new HeuristicRecruitingEmailParser(),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b"
                }),
            NullLogger<OllamaRecruitingEmailParser>.Instance);

        var parsed = await parser.ParseAsync(
            new InboxEmail
            {
                InboxAccount = new InboxAccount
                {
                    EmailAddress = "jlee@northgate.edu"
                },
                Sender = "\"Recruiting\" <recruiting@northgate.edu>",
                Subject = "Thank you for applying",
                ParsedBody = "Thank you for applying for the AI Intern role. We will review your materials soon."
            });

        Assert.Equal(ApplicationDefaults.UnknownCompany, parsed.Company);
        Assert.Equal("AI Intern", parsed.Role);
        Assert.True(parsed.ClassificationConfidence <= 0.55m);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            : this(request => Task.FromResult(responseFactory(request)))
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return await _responseFactory(request);
        }
    }

    private sealed class StubGuidanceStore(string guidance) : IRecruitingEmailGuidanceStore
    {
        public Task<string> ReadGuidanceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(guidance);
        }

        public Task AppendFeedbackAsync(
            RecruitingEmailGuidanceFeedback feedback,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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
