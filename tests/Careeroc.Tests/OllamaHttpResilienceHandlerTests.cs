using System.Net;
using System.Text;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Careeroc.Tests;

public sealed class OllamaHttpResilienceHandlerTests
{
    [Fact]
    public async Task SendAsync_RetriesTransientServerFailures()
    {
        var attempts = 0;
        var client = CreateClient(
            new OllamaEmailExtractionOptions
            {
                MaxConcurrentRequests = 4,
                RetryCount = 1,
                RetryDelayMilliseconds = 0
            },
            async request =>
            {
                attempts++;
                var body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync();
                Assert.Equal("{\"model\":\"gemma\"}", body);

                return attempts == 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
            });

        var response = await client.PostAsync(
            "https://ollama.example/api/chat",
            new StringContent("{\"model\":\"gemma\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task SendAsync_LimitsConcurrentOllamaRequests()
    {
        var activeRequests = 0;
        var maxActiveRequests = 0;
        var client = CreateClient(
            new OllamaEmailExtractionOptions
            {
                MaxConcurrentRequests = 1,
                RetryCount = 0
            },
            async _ =>
            {
                var active = Interlocked.Increment(ref activeRequests);
                maxActiveRequests = Math.Max(maxActiveRequests, active);
                await Task.Delay(25);
                Interlocked.Decrement(ref activeRequests);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        await Task.WhenAll(
            client.GetAsync("https://ollama.example/api/chat"),
            client.GetAsync("https://ollama.example/api/chat"));

        Assert.Equal(1, maxActiveRequests);
    }

    private static HttpClient CreateClient(
        OllamaEmailExtractionOptions options,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
    {
        var optionsWrapper = Options.Create(options);
        var limiter = new OllamaHttpConcurrencyLimiter(optionsWrapper);
        var handler = new OllamaHttpResilienceHandler(
            limiter,
            optionsWrapper,
            NullLogger<OllamaHttpResilienceHandler>.Instance)
        {
            InnerHandler = new StubHttpMessageHandler(responseFactory)
        };

        return new HttpClient(handler);
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
}
