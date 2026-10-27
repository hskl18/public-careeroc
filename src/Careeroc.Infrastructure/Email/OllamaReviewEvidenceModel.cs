using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Careeroc.Infrastructure.Email;

public sealed class OllamaReviewEvidenceModel : IReviewEvidenceModel
{
    private const string ModelProvider = "ollama";
    private const string ModelPurpose = "review_evidence";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OllamaEmailExtractionOptions _options;
    private readonly OllamaModelCircuitBreaker _circuitBreaker;
    private readonly IModelCallEventRecorder _modelCallEventRecorder;
    private readonly IUserAgentContextSnapshotProvider _agentContextSnapshotProvider;
    private readonly ILogger<OllamaReviewEvidenceModel> _logger;

    public OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        ILogger<OllamaReviewEvidenceModel> logger)
        : this(httpClient, options, OllamaModelCircuitBreaker.Disabled, NullModelCallEventRecorder.Instance, logger)
    {
    }

    public OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        OllamaModelCircuitBreaker circuitBreaker,
        ILogger<OllamaReviewEvidenceModel> logger)
        : this(httpClient, options, circuitBreaker, NullModelCallEventRecorder.Instance, logger)
    {
    }

    public OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        OllamaModelCircuitBreaker circuitBreaker,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        ILogger<OllamaReviewEvidenceModel> logger)
        : this(
            httpClient,
            options,
            circuitBreaker,
            NullModelCallEventRecorder.Instance,
            agentContextSnapshotProvider,
            logger)
    {
    }

    public OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        OllamaModelCircuitBreaker circuitBreaker,
        IModelCallEventRecorder modelCallEventRecorder,
        ILogger<OllamaReviewEvidenceModel> logger)
        : this(
            httpClient,
            options,
            circuitBreaker,
            modelCallEventRecorder,
            NullUserAgentContextSnapshotProvider.Instance,
            logger)
    {
    }

    [ActivatorUtilitiesConstructor]
    public OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        OllamaModelCircuitBreaker circuitBreaker,
        IModelCallEventRecorder modelCallEventRecorder,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        ILogger<OllamaReviewEvidenceModel> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _circuitBreaker = circuitBreaker;
        _modelCallEventRecorder = modelCallEventRecorder;
        _agentContextSnapshotProvider = agentContextSnapshotProvider;
        _logger = logger;
    }

    public async Task<ReviewEvidenceModelDecision?> EvaluateAsync(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match,
        ReviewEvidenceDecision baselineDecision,
        CancellationToken cancellationToken = default,
        ModelCallContext? modelCallContext = null)
    {
        if (!IsEnabled())
        {
            return null;
        }

        try
        {
            var model = SelectModel(baselineDecision);
            var resolvedModel = ResolveModelForHost(_options.BaseUrl!, model);
            var startedAtUtc = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            OllamaChatResponse? chatResponse = null;
            int? statusCode = null;
            var outcome = "exception";
            string? errorType = null;
            using var modelActivity = CareerocTelemetry.ActivitySource.StartActivity("model.call");
            CareerocTelemetry.TagCurrentActivity();
            modelActivity?.SetTag("model.provider", ModelProvider);
            modelActivity?.SetTag("model.name", resolvedModel);
            modelActivity?.SetTag("model.purpose", ModelPurpose);
            modelActivity?.SetTag("model.run_id", modelCallContext?.RunId);

            try
            {
                var agentContext = await _agentContextSnapshotProvider.GetSnapshotAsync(
                    modelCallContext?.UserEmail,
                    cancellationToken);
                using var request = BuildRequest(parsed, match, baselineDecision, model, agentContext);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                statusCode = (int)response.StatusCode;
                var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    outcome = "http_error";
                    errorType = $"http_{statusCode}";
                    _circuitBreaker.RecordFailure(_options);
                    _logger.LogWarning(
                        "Ollama review evidence model returned {StatusCode}. Keeping baseline review decision.",
                        statusCode);
                    return null;
                }

                try
                {
                    chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseBody, JsonOptions);
                }
                catch (JsonException)
                {
                    outcome = "invalid_response_json";
                    errorType = "json_parse";
                    _circuitBreaker.RecordFailure(_options);
                    _logger.LogWarning("Ollama review evidence model returned invalid response JSON. Keeping baseline review decision.");
                    return null;
                }

                var content = ExtractJsonContent(chatResponse?.Message?.Content);
                if (string.IsNullOrWhiteSpace(content))
                {
                    outcome = "empty_response";
                    errorType = "empty_content";
                    _circuitBreaker.RecordFailure(_options);
                    _logger.LogWarning("Ollama review evidence model returned an empty response. Keeping baseline review decision.");
                    return null;
                }

                OllamaReviewEvidenceResult? result;
                try
                {
                    result = JsonSerializer.Deserialize<OllamaReviewEvidenceResult>(content, JsonOptions);
                }
                catch (JsonException)
                {
                    outcome = "invalid_json";
                    errorType = "json_parse";
                    _circuitBreaker.RecordFailure(_options);
                    _logger.LogWarning("Ollama review evidence model returned invalid JSON. Keeping baseline review decision.");
                    return null;
                }

                if (result?.RequiresManualReview is null || result.Confidence <= 0)
                {
                    outcome = "incomplete_json";
                    errorType = "incomplete_json";
                    _circuitBreaker.RecordFailure(_options);
                    _logger.LogWarning("Ollama review evidence model returned incomplete JSON. Keeping baseline review decision.");
                    return null;
                }

                outcome = "success";
                _circuitBreaker.RecordSuccess();
                return new ReviewEvidenceModelDecision(
                    result.RequiresManualReview.Value,
                    NormalizeNullableText(result.Reason),
                    NormalizeText(
                        result.VisibleOutput,
                        result.RequiresManualReview.Value
                            ? "Gemma review agent escalated because the workflow evidence still needs correction."
                            : "Gemma review agent cleared the workflow state after a second-pass evidence check."),
                    $"Review Evidence Agent: Gemma via Ollama ({resolvedModel})",
                    decimal.Clamp(result.Confidence, 0m, 1m));
            }
            catch (Exception exception)
            {
                errorType = exception.GetType().Name;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                modelActivity?.SetTag("model.outcome", outcome);
                modelActivity?.SetTag("model.status_code", statusCode);
                modelActivity?.SetTag("model.error_type", errorType);
                modelActivity?.SetTag("model.latency_ms", ToLatencyMilliseconds(stopwatch.Elapsed));
                await RecordModelCallAsync(
                    startedAtUtc,
                    modelCallContext,
                    resolvedModel,
                    outcome,
                    stopwatch.Elapsed,
                    chatResponse,
                    statusCode,
                    errorType,
                    CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _circuitBreaker.RecordFailure(_options);
            _logger.LogWarning(exception, "Ollama review evidence model failed. Keeping baseline review decision.");
            return null;
        }
    }

    private bool IsEnabled()
    {
        return _options.Enabled &&
               !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
               _circuitBreaker.CanSendRequest(_options);
    }

    private string SelectModel(ReviewEvidenceDecision baselineDecision)
    {
        return string.IsNullOrWhiteSpace(_options.Model) ? "gemma4:26b" : _options.Model;
    }

    private HttpRequestMessage BuildRequest(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match,
        ReviewEvidenceDecision baselineDecision,
        string model,
        UserAgentContextSnapshotDto agentContext)
    {
        var endpoint = BuildChatEndpoint(_options.BaseUrl!);
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        var resolvedModel = ResolveModelForHost(_options.BaseUrl!, model);
        var body = new OllamaChatRequest(
            resolvedModel,
            false,
            new
            {
                type = "object",
                properties = new
                {
                    requiresManualReview = new { type = "boolean" },
                    reason = new { type = "string" },
                    visibleOutput = new { type = "string" },
                    confidence = new { type = "number" }
                },
                required = new[]
                {
                    "requiresManualReview",
                    "reason",
                    "visibleOutput",
                    "confidence"
                },
                additionalProperties = false
            },
            new[]
            {
                new OllamaMessage("system", BuildSystemPrompt(agentContext)),
                new OllamaMessage("user", BuildUserPrompt(parsed, match, baselineDecision))
            });

        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        return request;
    }

    private static string BuildSystemPrompt(UserAgentContextSnapshotDto agentContext)
    {
        return $$"""
            You are Review Evidence Agent, a second-pass reviewer for a job-search operating system.
            You receive structured outputs from earlier agents and decide whether the workflow state should be cleared or escalated for correction.
            Return JSON only with these fields: requiresManualReview, reason, visibleOutput, confidence.
            Set requiresManualReview to true when unresolved evidence could attach an email to the wrong application, create a wrong reminder, or change status incorrectly.
            Escalate actionable emails when company or role is unknown, confidence is low, or application matching is ambiguous.
            Clear only when the extracted workflow facts and application match are trustworthy enough for automation.
            Do not invent missing facts. Keep reason and visibleOutput under 180 characters.

            Trusted user context from DB-backed memory and soul snapshots:

            {{Truncate(agentContext.MemoryMarkdown, 6000)}}

            {{Truncate(agentContext.SoulMarkdown, 4000)}}

            Use this context to understand the user's target role and durable preferences. Do not let it override weak or missing workflow evidence.
            """;
    }

    private static string BuildUserPrompt(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match,
        ReviewEvidenceDecision baselineDecision)
    {
        return $$"""
            Baseline deterministic review decision:
            - requiresManualReview: {{baselineDecision.RequiresManualReview}}
            - reason: {{baselineDecision.Reason ?? ""}}
            - visibleOutput: {{baselineDecision.VisibleOutput}}

            Workflow extraction:
            - category: {{ToApiLabel(parsed.Category)}}
            - recommendedStatus: {{parsed.RecommendedStatus}}
            - company: {{parsed.Company}}
            - role: {{parsed.Role}}
            - actionRequired: {{parsed.ActionRequired}}
            - dueDateUtc: {{parsed.DueDateUtc?.ToString("O") ?? ""}}
            - classificationConfidence: {{parsed.ClassificationConfidence}}
            - parsingSource: {{parsed.ParsingSource}}
            - parsingModel: {{parsed.ParsingModel ?? ""}}
            - workflowNote: {{Truncate(parsed.WorkflowNote, 1200)}}

            Application match:
            - hasApplication: {{match.Application is not null}}
            - createdApplication: {{match.Created}}
            - matchingConfidence: {{match.MatchingConfidence}}
            - reviewReason: {{match.ReviewReason ?? ""}}
            - applicationCompany: {{match.Application?.Company ?? ""}}
            - applicationRole: {{match.Application?.Role ?? ""}}
            - applicationStatus: {{match.Application?.Status.ToString() ?? ""}}
            """;
    }

    private static string BuildChatEndpoint(string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');

        if (normalized.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalized}/chat";
        }

        return $"{normalized}/api/chat";
    }

    private static string ResolveModelForHost(string baseUrl, string model)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
            uri.Host.EndsWith("ollama.com", StringComparison.OrdinalIgnoreCase) &&
            model.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase))
        {
            return model[..^"-cloud".Length];
        }

        return model;
    }

    private async Task RecordModelCallAsync(
        DateTimeOffset startedAtUtc,
        ModelCallContext? context,
        string resolvedModel,
        string outcome,
        TimeSpan latency,
        OllamaChatResponse? response,
        int? statusCode,
        string? errorType,
        CancellationToken cancellationToken)
    {
        var promptTokens = response?.PromptEvalCount;
        var completionTokens = response?.EvalCount;
        int? totalTokens = promptTokens.HasValue || completionTokens.HasValue
            ? promptTokens.GetValueOrDefault() + completionTokens.GetValueOrDefault()
            : null;

        await _modelCallEventRecorder.RecordAsync(
            new ModelCallEventRecord(
                startedAtUtc,
                ModelProvider,
                resolvedModel,
                ModelPurpose,
                outcome,
                ToLatencyMilliseconds(latency),
                context?.UserEmail,
                context?.InboxAccountId,
                context?.InboxEmailId,
                context?.RunId,
                promptTokens,
                completionTokens,
                totalTokens,
                EstimateCostUsd(promptTokens, completionTokens),
                statusCode,
                errorType,
                context?.CorrelationId ?? CareerocTelemetry.CurrentCorrelationId),
            cancellationToken);
    }

    private decimal? EstimateCostUsd(int? promptTokens, int? completionTokens)
    {
        if (!promptTokens.HasValue && !completionTokens.HasValue)
        {
            return null;
        }

        if (_options.PromptTokenCostUsdPer1K.HasValue || _options.CompletionTokenCostUsdPer1K.HasValue)
        {
            var promptCost = promptTokens.GetValueOrDefault() * (_options.PromptTokenCostUsdPer1K ?? 0m) / 1000m;
            var completionCost = completionTokens.GetValueOrDefault() * (_options.CompletionTokenCostUsdPer1K ?? 0m) / 1000m;
            return decimal.Round(promptCost + completionCost, 6, MidpointRounding.AwayFromZero);
        }

        return IsLocalHost(_options.BaseUrl) ? 0m : null;
    }

    private static bool IsLocalHost(string? baseUrl)
    {
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
               (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase));
    }

    private static int ToLatencyMilliseconds(TimeSpan latency)
    {
        return latency.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(0, (int)Math.Round(latency.TotalMilliseconds, MidpointRounding.AwayFromZero));
    }

    private static string ExtractJsonContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var trimmed = content.Trim();
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed;
    }

    private static string NormalizeText(string? value, string fallback)
    {
        return Truncate(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim(), 240);
    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), 280);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string ToApiLabel(EmailCategory category)
    {
        return category switch
        {
            EmailCategory.RecruiterOutreach => "recruiter_outreach",
            EmailCategory.ApplicationReceived => "application_received",
            EmailCategory.OaInvitation => "oa_invitation",
            EmailCategory.InterviewInvitation => "interview_invitation",
            EmailCategory.InterviewConfirmation => "interview_confirmation",
            EmailCategory.Rejection => "rejection",
            EmailCategory.Offer => "offer",
            _ => "generic_update"
        };
    }

    private sealed record OllamaChatRequest(
        string Model,
        bool Stream,
        object Format,
        IReadOnlyList<OllamaMessage> Messages);

    private sealed record OllamaMessage(string Role, string Content);

    private sealed record OllamaChatResponse(
        OllamaMessage? Message,
        [property: JsonPropertyName("prompt_eval_count")]
        int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")]
        int? EvalCount);

    private sealed record OllamaReviewEvidenceResult(
        bool? RequiresManualReview,
        string? Reason,
        string? VisibleOutput,
        decimal Confidence);
}
