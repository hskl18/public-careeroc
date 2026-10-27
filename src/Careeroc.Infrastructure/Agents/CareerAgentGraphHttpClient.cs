using System.Net.Http.Json;
using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Careeroc.Infrastructure.Agents;

public sealed class CareerAgentGraphHttpClient : ICareerAgentGraphClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AgentRuntimeOptions _options;
    private readonly ILogger<CareerAgentGraphHttpClient> _logger;

    public CareerAgentGraphHttpClient(
        HttpClient httpClient,
        IOptions<AgentRuntimeOptions> options,
        ILogger<CareerAgentGraphHttpClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<CareerAgentGraphRunOutcome> ReviewResumeMailboxAsync(
        CareerAgentGraphReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "/v1/resume-mailbox-review")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            },
            request.ThreadId,
            cancellationToken);
    }

    // Resumes a review that paused at the graph's human_review interrupt.
    public Task<CareerAgentGraphRunOutcome> DecideReviewAsync(
        string threadId,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/resume-mailbox-review/{Uri.EscapeDataString(threadId)}/decision")
            {
                Content = JsonContent.Create(new { approved }, options: JsonOptions)
            },
            threadId,
            cancellationToken);
    }

    private async Task<CareerAgentGraphRunOutcome> SendAsync(
        Func<HttpRequestMessage> createRequest,
        string threadId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return CareerAgentGraphRunOutcome.Skipped();
        }

        try
        {
            using var httpRequest = createRequest();
            if (!string.IsNullOrWhiteSpace(_options.SharedSecret))
            {
                httpRequest.Headers.Add("X-Careeroc-Agent-Secret", _options.SharedSecret);
            }

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var reason = $"Agent runtime returned HTTP {(int)response.StatusCode}.";
                _logger.LogWarning("{Reason} ThreadId={ThreadId}", reason, threadId);
                return CareerAgentGraphRunOutcome.Failed(reason);
            }

            var result = await response.Content.ReadFromJsonAsync<CareerAgentGraphReviewResponse>(
                JsonOptions,
                cancellationToken);
            if (result is null ||
                string.IsNullOrWhiteSpace(result.Summary) ||
                !string.Equals(result.ThreadId, threadId, StringComparison.Ordinal))
            {
                const string reason = "Agent runtime returned an invalid or mismatched response.";
                _logger.LogWarning("{Reason} ThreadId={ThreadId}", reason, threadId);
                return CareerAgentGraphRunOutcome.Failed(reason);
            }

            return CareerAgentGraphRunOutcome.Succeeded(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            const string reason = "Agent runtime request timed out.";
            _logger.LogWarning("{Reason} ThreadId={ThreadId}", reason, threadId);
            return CareerAgentGraphRunOutcome.Failed(reason);
        }
        catch (HttpRequestException exception)
        {
            const string reason = "Agent runtime was unavailable.";
            _logger.LogWarning(exception, "{Reason} ThreadId={ThreadId}", reason, threadId);
            return CareerAgentGraphRunOutcome.Failed(reason);
        }
        catch (JsonException exception)
        {
            const string reason = "Agent runtime returned malformed JSON.";
            _logger.LogWarning(exception, "{Reason} ThreadId={ThreadId}", reason, threadId);
            return CareerAgentGraphRunOutcome.Failed(reason);
        }
    }
}
