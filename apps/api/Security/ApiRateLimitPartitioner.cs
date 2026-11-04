using Careeroc.Api.Endpoints;

namespace Careeroc.Api.Security;

internal static class ApiRateLimitPartitioner
{
    private static readonly PathString GmailOAuthCallbackPath = new("/api/inbox-accounts/gmail/oauth/callback");

    public static bool IsOAuthCallback(HttpRequest request)
    {
        return request.Path.Equals(GmailOAuthCallbackPath);
    }

    public static string ResolvePartitionKey(HttpContext context)
    {
        if (IsOAuthCallback(context.Request))
        {
            return $"oauth:{GetRemoteIp(context)}";
        }

        var userEmail = RequestUserContext.GetUserEmail(context.Request);
        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            return $"user:{userEmail.Trim().ToLowerInvariant()}";
        }

        return $"ip:{GetRemoteIp(context)}";
    }

    public static string GetRemoteIp(HttpContext context)
    {
        return string.IsNullOrWhiteSpace(context.Connection.RemoteIpAddress?.ToString())
            ? "unknown"
            : context.Connection.RemoteIpAddress!.ToString();
    }
}
