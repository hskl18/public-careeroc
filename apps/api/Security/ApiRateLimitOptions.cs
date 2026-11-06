namespace Careeroc.Api.Security;

internal sealed class ApiRateLimitOptions
{
    public const string SectionName = "Security:RateLimiting";

    public int UserPermitLimit { get; set; } = 120;

    public int AnonymousPermitLimit { get; set; } = 30;

    public int WindowSeconds { get; set; } = 60;

    public int QueueLimit { get; set; } = 0;

    public ApiRateLimitOptions Normalize()
    {
        return new ApiRateLimitOptions
        {
            UserPermitLimit = Math.Max(1, UserPermitLimit),
            AnonymousPermitLimit = Math.Max(1, AnonymousPermitLimit),
            WindowSeconds = Math.Max(1, WindowSeconds),
            QueueLimit = Math.Max(0, QueueLimit)
        };
    }
}

public static class AgentReviewRateLimit
{
    public const string PolicyName = "agent-review";
    public const int PermitLimit = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
}
