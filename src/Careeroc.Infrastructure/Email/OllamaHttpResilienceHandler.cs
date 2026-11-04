using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Careeroc.Infrastructure.Email;

public sealed class OllamaHttpResilienceHandler : DelegatingHandler
{
    private readonly OllamaHttpConcurrencyLimiter _concurrencyLimiter;
    private readonly OllamaEmailExtractionOptions _options;
    private readonly ILogger<OllamaHttpResilienceHandler> _logger;

    public OllamaHttpResilienceHandler(
        OllamaHttpConcurrencyLimiter concurrencyLimiter,
        IOptions<OllamaEmailExtractionOptions> options,
        ILogger<OllamaHttpResilienceHandler> logger)
    {
        _concurrencyLimiter = concurrencyLimiter;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var lease = await _concurrencyLimiter.AcquireAsync(cancellationToken);
        var snapshot = await BufferedHttpRequestSnapshot.CreateAsync(request, cancellationToken);
        var retryCount = Math.Clamp(_options.RetryCount, 0, 5);
        var retryDelay = TimeSpan.FromMilliseconds(Math.Clamp(_options.RetryDelayMilliseconds, 0, 5000));

        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            using var attemptRequest = snapshot.CreateRequest();
            try
            {
                var response = await base.SendAsync(attemptRequest, cancellationToken);
                if (!ShouldRetry(response.StatusCode) || attempt == retryCount)
                {
                    return response;
                }

                _logger.LogWarning(
                    "Ollama HTTP request returned {StatusCode}; retrying attempt {Attempt}/{RetryCount}.",
                    (int)response.StatusCode,
                    attempt + 1,
                    retryCount);
                response.Dispose();
            }
            catch (HttpRequestException exception) when (attempt < retryCount)
            {
                _logger.LogWarning(
                    exception,
                    "Ollama HTTP request failed; retrying attempt {Attempt}/{RetryCount}.",
                    attempt + 1,
                    retryCount);
            }

            if (retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        using var finalRequest = snapshot.CreateRequest();
        return await base.SendAsync(finalRequest, cancellationToken);
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.TooManyRequests ||
               (int)statusCode >= 500;
    }

    private sealed class BufferedHttpRequestSnapshot
    {
        private readonly HttpMethod _method;
        private readonly Uri? _requestUri;
        private readonly Version _version;
        private readonly HttpVersionPolicy _versionPolicy;
        private readonly IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> _headers;
        private readonly byte[]? _contentBytes;
        private readonly IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> _contentHeaders;

        private BufferedHttpRequestSnapshot(
            HttpMethod method,
            Uri? requestUri,
            Version version,
            HttpVersionPolicy versionPolicy,
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> headers,
            byte[]? contentBytes,
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> contentHeaders)
        {
            _method = method;
            _requestUri = requestUri;
            _version = version;
            _versionPolicy = versionPolicy;
            _headers = headers;
            _contentBytes = contentBytes;
            _contentHeaders = contentHeaders;
        }

        public static async Task<BufferedHttpRequestSnapshot> CreateAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[]? contentBytes = null;
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> contentHeaders = [];
            if (request.Content is not null)
            {
                contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                contentHeaders = request.Content.Headers
                    .Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray()))
                    .ToList();
            }

            return new BufferedHttpRequestSnapshot(
                request.Method,
                request.RequestUri,
                request.Version,
                request.VersionPolicy,
                request.Headers
                    .Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray()))
                    .ToList(),
                contentBytes,
                contentHeaders);
        }

        public HttpRequestMessage CreateRequest()
        {
            var request = new HttpRequestMessage(_method, _requestUri)
            {
                Version = _version,
                VersionPolicy = _versionPolicy
            };

            foreach (var header in _headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (_contentBytes is not null)
            {
                request.Content = new ByteArrayContent(_contentBytes);
                foreach (var header in _contentHeaders)
                {
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return request;
        }
    }
}
