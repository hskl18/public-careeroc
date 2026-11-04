using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Careeroc.Infrastructure.Resume;

public sealed class OllamaResumeVisionTextExtractor : IResumeVisionTextExtractor
{
    private const string ModelProvider = "ollama";
    private const string ModelPurpose = "resume_vision_extraction";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OllamaEmailExtractionOptions _options;
    private readonly OllamaModelCircuitBreaker _circuitBreaker;
    private readonly IModelCallEventRecorder _modelCallEventRecorder;
    private readonly ILogger<OllamaResumeVisionTextExtractor> _logger;

    public OllamaResumeVisionTextExtractor(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        ILogger<OllamaResumeVisionTextExtractor> logger)
        : this(httpClient, options, OllamaModelCircuitBreaker.Disabled, NullModelCallEventRecorder.Instance, logger)
    {
    }

    [ActivatorUtilitiesConstructor]
    public OllamaResumeVisionTextExtractor(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        OllamaModelCircuitBreaker circuitBreaker,
        IModelCallEventRecorder modelCallEventRecorder,
        ILogger<OllamaResumeVisionTextExtractor> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _circuitBreaker = circuitBreaker;
        _modelCallEventRecorder = modelCallEventRecorder;
        _logger = logger;
    }

    public async Task<ResumeVisionTextExtractionDecision?> ExtractAsync(
        ResumeVisionTextExtractionRequest request,
        CancellationToken cancellationToken = default,
        ModelCallContext? modelCallContext = null)
    {
        if (!IsEnabled())
        {
            return null;
        }

        var model = ResolveModelForHost(_options.BaseUrl!, _options.Model);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        OllamaChatResponse? chatResponse = null;
        int? statusCode = null;
        var outcome = "exception";
        string? errorType = null;
        using var modelActivity = CareerocTelemetry.ActivitySource.StartActivity("model.call");
        CareerocTelemetry.TagCurrentActivity();
        modelActivity?.SetTag("model.provider", ModelProvider);
        modelActivity?.SetTag("model.name", model);
        modelActivity?.SetTag("model.purpose", ModelPurpose);
        modelActivity?.SetTag("model.run_id", modelCallContext?.RunId);

        try
        {
            using var httpRequest = BuildRequest(request, model);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            statusCode = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                outcome = "http_error";
                errorType = $"http_{statusCode}";
                _circuitBreaker.RecordFailure(_options);
                return null;
            }

            chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseBody, JsonOptions);
            var content = ExtractJsonContent(chatResponse?.Message?.Content);
            if (string.IsNullOrWhiteSpace(content))
            {
                outcome = "empty_response";
                errorType = "empty_content";
                _circuitBreaker.RecordFailure(_options);
                return null;
            }

            OllamaResumeVisionExtractionResult? result;
            try
            {
                result = JsonSerializer.Deserialize<OllamaResumeVisionExtractionResult>(content, JsonOptions);
            }
            catch (JsonException)
            {
                outcome = "invalid_json";
                errorType = "json_parse";
                _circuitBreaker.RecordFailure(_options);
                return null;
            }

            var extractedText = result?.ExtractedText?.Trim();
            if (string.IsNullOrWhiteSpace(extractedText) || result!.Confidence <= 0)
            {
                outcome = "incomplete_json";
                errorType = "incomplete_json";
                _circuitBreaker.RecordFailure(_options);
                return null;
            }

            outcome = "success";
            _circuitBreaker.RecordSuccess();
            return new ResumeVisionTextExtractionDecision(
                extractedText,
                decimal.Clamp(result.Confidence, 0m, 1m),
                $"Resume Vision Agent: Gemma via Ollama ({model})");
        }
        catch (Exception exception)
        {
            errorType = exception.GetType().Name;
            _circuitBreaker.RecordFailure(_options);
            _logger.LogWarning(exception, "Ollama resume vision extraction failed.");
            return null;
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
                model,
                outcome,
                stopwatch.Elapsed,
                chatResponse,
                statusCode,
                errorType,
                cancellationToken);
        }
    }

    private bool IsEnabled()
    {
        return _options.Enabled &&
               !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
               _circuitBreaker.CanSendRequest(_options);
    }

    private HttpRequestMessage BuildRequest(ResumeVisionTextExtractionRequest request, string model)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildChatEndpoint(_options.BaseUrl!));
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        var body = new OllamaChatRequest(
            model,
            false,
            new
            {
                type = "object",
                properties = new
                {
                    extractedText = new { type = "string" },
                    confidence = new { type = "number" }
                },
                required = new[] { "extractedText", "confidence" },
                additionalProperties = false
            },
            [
                new OllamaVisionMessage("system", BuildSystemPrompt(), null),
                new OllamaVisionMessage("user", BuildUserPrompt(request), [request.Base64Image])
            ]);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");
        return httpRequest;
    }

    private static string BuildSystemPrompt()
    {
        return """
            You are Resume Vision Extraction Agent for a job-search operating system.
            Read the image and return JSON only.
            Extract visible resume, recruiting artifact, screenshot, or document text faithfully in reading order.
            Preserve section headings, dates, company names, project names, emails, links, skills, and bullet text.
            Do not summarize, evaluate, translate, infer, or invent missing text.
            Set confidence below 0.55 if the image is unreadable or not job-search evidence.
            """;
    }

    private static string BuildUserPrompt(ResumeVisionTextExtractionRequest request)
    {
        return $$"""
            File name: {{request.FileName}}
            Content type: {{request.ContentType}}
            Source kind: {{request.SourceKind}}

            Return:
            {
              "extractedText": "full visible text",
              "confidence": 0.0
            }
            """;
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
            return promptCost + completionCost;
        }

        return null;
    }

    private static string BuildChatEndpoint(string baseUrl)
    {
        return $"{baseUrl.TrimEnd('/')}/api/chat";
    }

    private static string ResolveModelForHost(string baseUrl, string model)
    {
        if (IsLocalHost(baseUrl) &&
            model.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase))
        {
            return model[..^"-cloud".Length];
        }

        return model;
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
        return firstBrace >= 0 && lastBrace > firstBrace
            ? trimmed[firstBrace..(lastBrace + 1)]
            : trimmed;
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

    private sealed record OllamaChatRequest(
        string Model,
        bool Stream,
        object Format,
        IReadOnlyList<OllamaVisionMessage> Messages);

    private sealed record OllamaVisionMessage(
        string Role,
        string Content,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<string>? Images);

    private sealed record OllamaChatResponse(
        OllamaVisionMessage? Message,
        [property: JsonPropertyName("prompt_eval_count")]
        int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")]
        int? EvalCount);

    private sealed record OllamaResumeVisionExtractionResult(
        string? ExtractedText,
        decimal Confidence);
}
