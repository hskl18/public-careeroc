namespace Careeroc.Application.Models;

public sealed class AgentRuntimeOptions
{
    public const string SectionName = "AgentRuntime";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:8123";

    public string SharedSecret { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;
}
