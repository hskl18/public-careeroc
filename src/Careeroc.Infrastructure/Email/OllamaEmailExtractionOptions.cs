namespace Careeroc.Infrastructure.Email;

public sealed class OllamaEmailExtractionOptions
{
    public bool Enabled { get; set; }

    public string? BaseUrl { get; set; }

    public string Model { get; set; } = "gemma4:31b";

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 90;

    public int MaxConcurrentRequests { get; set; } = 4;

    public int RetryCount { get; set; } = 1;

    public int RetryDelayMilliseconds { get; set; } = 250;

    public decimal MinimumConfidence { get; set; } = 0.60m;

    public int FailureCircuitBreakerThreshold { get; set; } = 5;

    public int FailureCircuitBreakerCooldownSeconds { get; set; } = 300;

    public decimal? PromptTokenCostUsdPer1K { get; set; }

    public decimal? CompletionTokenCostUsdPer1K { get; set; }

    public string GuidanceFilePath { get; set; } = "docs/agent-memory/recruiting-email-playbook.md";

    // Empty turns learning from corrections off. The file is shared by everyone on the install, so
    // only single-user installs should set it; Development does.
    public string UserFeedbackFilePath { get; set; } = string.Empty;

    public int GuidanceMaxCharacters { get; set; } = 16000;
}
