using System.Net;
using System.Text;
using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Infrastructure.Email;
using Careeroc.Infrastructure.Resume;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Careeroc.Tests;

public sealed class OllamaResumeVisionTextExtractorTests
{
    [Fact]
    public async Task ExtractAsync_SendsImageAndReturnsExtractedText()
    {
        string? requestBody = null;
        var recorder = new RecordingModelCallEventRecorder();
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
                            "content": "{\"extractedText\":\"Alex Rivera\\nalex@example.com\\nEducation: Cascadia State\\nExperience: Software Engineer Intern\\nProject: Careeroc\\nSkills: TypeScript, React, C#, SQL\",\"confidence\":0.88}"
                          },
                          "prompt_eval_count": 80,
                          "eval_count": 30
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            });
        var extractor = new OllamaResumeVisionTextExtractor(
            new HttpClient(handler),
            Options.Create(
                new OllamaEmailExtractionOptions
                {
                    Enabled = true,
                    BaseUrl = "http://localhost:11434",
                    Model = "gemma4:26b"
                }),
            OllamaModelCircuitBreaker.Disabled,
            recorder,
            NullLogger<OllamaResumeVisionTextExtractor>.Instance);

        var decision = await extractor.ExtractAsync(
            new ResumeVisionTextExtractionRequest(
                "resume.png",
                "image/png",
                Convert.ToBase64String([1, 2, 3, 4]),
                "image"),
            modelCallContext: new ModelCallContext("user@example.com", null, null, "vision-run"));

        Assert.NotNull(decision);
        Assert.Contains("Alex Rivera", decision.ExtractedText, StringComparison.Ordinal);
        Assert.Equal(0.88m, decision.Confidence);
        Assert.Contains("Resume Vision Agent", decision.AgentName, StringComparison.Ordinal);
        Assert.NotNull(requestBody);
        using var json = JsonDocument.Parse(requestBody);
        var userMessage = json.RootElement.GetProperty("messages")[1];
        Assert.Equal("user", userMessage.GetProperty("role").GetString());
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4]), userMessage.GetProperty("images")[0].GetString());
        var record = Assert.Single(recorder.Records);
        Assert.Equal("resume_vision_extraction", record.Purpose);
        Assert.Equal("success", record.Outcome);
        Assert.Equal("user@example.com", record.UserEmail);
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
}
